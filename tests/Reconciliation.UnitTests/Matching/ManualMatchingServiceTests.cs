using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Reconciliation.Application.Auditing;
using Reconciliation.Application.Common;
using Reconciliation.Application.Matching;
using Reconciliation.Domain.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;
using Reconciliation.Domain.ValueObjects;
using Reconciliation.Infrastructure.Locking;
using Reconciliation.Infrastructure.Persistence;

namespace Reconciliation.UnitTests.Matching;

public class ManualMatchingServiceTests
{
    private const string AcmeIban = "TR719999100000002000000011";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext _db = TestDb.Create();
    private readonly InMemoryDistributedLock _lock;
    private readonly StubCurrentUser _user = new("accountant@example.com");
    private readonly Customer _acme;
    private readonly Account _account;

    public ManualMatchingServiceTests()
    {
        _lock = new InMemoryDistributedLock(_clock);
        _acme = new Customer("Acme Textiles Ltd.", TaxNumber.Create("1234567890"), Iban.Create(AcmeIban), null, _clock.GetUtcNow());
        _account = new Account("Main", Iban.Create("TR549999000000001000000001"), "TRY", _clock.GetUtcNow());
        _db.AddRange(_acme, _account);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Manual_match_splits_a_payment_and_writes_the_audit_log()
    {
        var transaction = AddTransaction(1_500m, "Transfer");
        var first = AddInvoice("INV-1", 1_000m);
        var second = AddInvoice("INV-2", 800m);

        var matches = await CreateService().MatchAsync(transaction.Id,
            new ManualMatchRequest([new(first.Id, 1_000m), new(second.Id, 500m)], "Customer confirmed by phone"), CancellationToken.None);

        matches.Should().HaveCount(2).And.OnlyContain(m => m.Method == MatchMethod.Manual && m.Score == 100
            && m.CreatedBy == "accountant@example.com" && m.Reason == "Manual match: Customer confirmed by phone");
        (await Reload(transaction)).Status.Should().Be(TransactionStatus.Matched);
        (await Reload(second)).Status.Should().Be(InvoiceStatus.PartiallyPaid);
        _db.AuditEntries.Should().HaveCount(2).And.OnlyContain(a => a.Action == "match.created" && a.UserName == "accountant@example.com");
    }

    [Fact]
    public async Task Allocation_above_open_amount_saves_nothing()
    {
        var transaction = AddTransaction(1_500m, "Transfer");
        var first = AddInvoice("INV-1", 1_000m);
        var second = AddInvoice("INV-2", 100m);

        var act = () => CreateService().MatchAsync(transaction.Id,
            new ManualMatchRequest([new(first.Id, 1_000m), new(second.Id, 500m)], null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        _db.ChangeTracker.Clear();
        (await _db.Matches.CountAsync()).Should().Be(0);
        (await _db.AuditEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_invoice_is_reported()
    {
        var transaction = AddTransaction(100m, "Transfer");

        var act = () => CreateService().MatchAsync(transaction.Id,
            new ManualMatchRequest([new(Guid.NewGuid(), 100m)], null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Same_invoice_twice_is_a_validation_error()
    {
        var transaction = AddTransaction(100m, "Transfer");
        var invoice = AddInvoice("INV-1", 100m);

        var act = () => CreateService().MatchAsync(transaction.Id,
            new ManualMatchRequest([new(invoice.Id, 50m), new(invoice.Id, 50m)], null), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Reversing_an_automatic_match_stops_the_engine_from_redoing_it()
    {
        var invoice = AddInvoice("INV-2026-0001", 1_000m);
        var transaction = AddTransaction(1_000m, "Payment INV-2026-0001");
        await CreateReconciliation().RunAsync(CancellationToken.None);
        var autoMatch = await _db.Matches.SingleAsync();

        await CreateService().ReverseAsync(autoMatch.Id, new ReasonRequest("Wrong customer"), CancellationToken.None);
        var rerun = await CreateReconciliation().RunAsync(CancellationToken.None);

        rerun!.Processed.Should().Be(0);
        (await _db.Matches.CountAsync()).Should().Be(0);
        (await Reload(invoice)).Status.Should().Be(InvoiceStatus.Open);
        var reloaded = await Reload(transaction);
        reloaded.Status.Should().Be(TransactionStatus.Unmatched);
        reloaded.RequiresManualMatching.Should().BeTrue();
        _db.AuditEntries.Should().Contain(a => a.Action == "match.reversed" && a.Details!.Contains("Wrong customer"));
    }

    [Fact]
    public async Task Ignore_and_restore_are_audited()
    {
        var transaction = AddTransaction(-12.50m, "EFT fee");

        var ignored = await CreateService().IgnoreAsync(transaction.Id, new ReasonRequest("Bank fee"), CancellationToken.None);
        var restored = await CreateService().RestoreAsync(transaction.Id, CancellationToken.None);

        ignored.Status.Should().Be(TransactionStatus.Ignored);
        restored.Status.Should().Be(TransactionStatus.Unmatched);
        _db.AuditEntries.Select(a => a.Action).Should().BeEquivalentTo(["transaction.ignored", "transaction.restored"]);
    }

    [Fact]
    public async Task Manual_change_waits_for_a_running_reconciliation()
    {
        var transaction = AddTransaction(100m, "Transfer");
        await using var held = await _lock.TryAcquireAsync(ReconciliationService.LockName, TimeSpan.FromMinutes(1), CancellationToken.None);

        var act = () => CreateService().IgnoreAsync(transaction.Id, new ReasonRequest("x"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Counterparty_iban_is_masked_for_non_admins()
    {
        var transaction = AddTransaction(-5m, "Fee", iban: AcmeIban);

        var response = await CreateService().IgnoreAsync(transaction.Id, new ReasonRequest("Fee"), CancellationToken.None);

        response.CounterpartyIban.Should().Be("TR71******************0011");
    }

    private ManualMatchingService CreateService() =>
        new(_db, _lock, _user, new AuditTrail(_db, _user, _clock), _clock, new ManualMatchRequestValidator(), new ReasonRequestValidator());

    private ReconciliationService CreateReconciliation()
    {
        var system = new StubCurrentUser("system");
        return new ReconciliationService(_db, new MatchingEngine(), _lock, new AuditTrail(_db, system, _clock), _clock,
            Options.Create(new ReconciliationOptions()), NullLogger<ReconciliationService>.Instance);
    }

    private BankTransaction AddTransaction(decimal amount, string description, string? iban = null)
    {
        var transaction = new BankTransaction(_account.Id, Guid.NewGuid().ToString(), new DateOnly(2026, 11, 10), amount, "TRY",
            description, "ACME", iban, _clock.GetUtcNow());
        _db.BankTransactions.Add(transaction);
        _db.SaveChanges();
        return transaction;
    }

    private Invoice AddInvoice(string number, decimal amount)
    {
        var invoice = new Invoice(_acme.Id, number, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), amount, "TRY", _clock.GetUtcNow());
        _db.Invoices.Add(invoice);
        _db.SaveChanges();
        return invoice;
    }

    private async Task<T> Reload<T>(T entity) where T : Entity
    {
        _db.ChangeTracker.Clear();
        return await _db.Set<T>().SingleAsync(e => e.Id == entity.Id);
    }
}
