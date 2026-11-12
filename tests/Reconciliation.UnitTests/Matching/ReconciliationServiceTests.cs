using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Reconciliation.Application.Auditing;
using Reconciliation.Application.Matching;
using Reconciliation.Application.Webhooks;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;
using Reconciliation.FakeBank.Statements;
using Reconciliation.Infrastructure.Locking;
using Reconciliation.Infrastructure.Persistence;

namespace Reconciliation.UnitTests.Matching;

/// <summary>
/// Runs the real seed data and the FakeBank's scripted scenarios through the reconciliation service,
/// so the demo shown in Docker Compose is covered by a test.
/// </summary>
public class ReconciliationServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly InMemoryDistributedLock _lock;

    public ReconciliationServiceTests()
    {
        _lock = new InMemoryDistributedLock(_clock);
    }

    [Fact]
    public async Task Scenarios_end_up_matched_as_designed()
    {
        await SeedWithScenariosAsync();

        var result = await CreateService().RunAsync(CancellationToken.None);

        result.Should().BeEquivalentTo(new ReconciliationResult(Processed: 9, Matched: 7, Unmatched: 2, MatchedAmount: 103_800.49m));

        (await InvoiceStatuses()).Should().BeEquivalentTo(new Dictionary<string, InvoiceStatus>
        {
            ["INV-2026-0001"] = InvoiceStatus.Paid,          // FB-S001 exact
            ["INV-2026-0002"] = InvoiceStatus.Paid,          // FB-S002 partial + FB-S011 rest
            ["INV-2026-0003"] = InvoiceStatus.Paid,          // FB-S003 digits only
            ["INV-2026-0004"] = InvoiceStatus.Paid,          // FB-S004 overpayment
            ["INV-2026-0005"] = InvoiceStatus.Open,          // FB-S005 ambiguous, left for a person
            ["INV-2026-0006"] = InvoiceStatus.Open,
            ["INV-2026-0007"] = InvoiceStatus.Paid,          // FB-S006 tax number
            ["INV-2026-0008"] = InvoiceStatus.Paid,          // FB-S007 lower case
        });

        (await TransactionStatuses()).Should().BeEquivalentTo(new Dictionary<string, TransactionStatus>
        {
            ["FB-S001"] = TransactionStatus.Matched,
            ["FB-S002"] = TransactionStatus.Matched,
            ["FB-S003"] = TransactionStatus.Matched,
            ["FB-S004"] = TransactionStatus.PartiallyMatched, // 300.00 overpaid
            ["FB-S005"] = TransactionStatus.Unmatched,
            ["FB-S006"] = TransactionStatus.Matched,
            ["FB-S007"] = TransactionStatus.Matched,
            ["FB-S008"] = TransactionStatus.Unmatched,
            ["FB-S009"] = TransactionStatus.Unmatched,        // outgoing
            ["FB-S010"] = TransactionStatus.Unmatched,
            ["FB-S011"] = TransactionStatus.Matched,
        });

        _db.Matches.Should().OnlyContain(m => m.Method == MatchMethod.Automatic && m.CreatedBy == "system" && m.Score >= 80);
        _db.WebhookDeliveries.Should().HaveCount(7).And.OnlyContain(d => d.EventType == WebhookEvents.MatchCreated && d.Status == WebhookStatus.Pending);
    }

    [Fact]
    public async Task Second_run_changes_nothing()
    {
        await SeedWithScenariosAsync();
        await CreateService().RunAsync(CancellationToken.None);
        var matchCount = await _db.Matches.CountAsync();

        var second = await CreateService().RunAsync(CancellationToken.None);

        second.Should().BeEquivalentTo(new ReconciliationResult(2, 0, 2, 0m)); // only FB-S005 and FB-S008 are retried
        (await _db.Matches.CountAsync()).Should().Be(matchCount);
    }

    [Fact]
    public async Task Higher_threshold_leaves_weaker_proposals_for_review()
    {
        await SeedWithScenariosAsync();

        var result = await CreateService(threshold: 95).RunAsync(CancellationToken.None);

        // Only exact or partial with full number and IBAN reach 95: FB-S001, FB-S002, FB-S011.
        result!.Matched.Should().Be(3);
    }

    [Fact]
    public async Task Returns_null_while_another_run_holds_the_lock()
    {
        await using var held = await _lock.TryAcquireAsync(ReconciliationService.LockName, TimeSpan.FromMinutes(1), CancellationToken.None);

        (await CreateService().RunAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Suggestion_explains_an_ambiguous_transaction_without_storing_anything()
    {
        await SeedWithScenariosAsync();
        var ambiguous = await _db.BankTransactions.SingleAsync(t => t.ExternalId == "FB-S005");

        var suggestion = await CreateService().SuggestAsync(ambiguous.Id, CancellationToken.None);

        suggestion.HasProposal.Should().BeFalse();
        suggestion.Explanation.Should().StartWith("Ambiguous: 2 open invoices of Initech Software Ltd.");
        (await _db.Matches.CountAsync()).Should().Be(0);
    }

    private ReconciliationService CreateService(int threshold = 80) =>
        new(_db, new MatchingEngine(), _lock, new AuditTrail(_db, new StubCurrentUser("system"), _clock), new WebhookOutbox(_db, _clock), _clock, Options.Create(new ReconciliationOptions { AutoMatchThreshold = threshold }),
            NullLogger<ReconciliationService>.Instance);

    private async Task SeedWithScenariosAsync()
    {
        await DataSeeder.SeedAsync(_db, CancellationToken.None);
        var mainAccount = await _db.Accounts.SingleAsync(a => a.Name == "Main TRY Account");

        var bank = new StatementGenerator(_clock, Options.Create(new StatementOptions { NoiseProbability = 0 }));
        foreach (var line in bank.GetLines(mainAccount.Iban.Value, new DateOnly(2026, 1, 1)))
        {
            _db.BankTransactions.Add(new BankTransaction(mainAccount.Id, line.TransactionId, line.BookingDate, line.Amount,
                line.Currency, line.Description, line.CounterpartyName, line.CounterpartyIban, _clock.GetUtcNow()));
        }

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private Task<Dictionary<string, InvoiceStatus>> InvoiceStatuses() =>
        _db.Invoices.AsNoTracking().ToDictionaryAsync(i => i.Number, i => i.Status);

    private Task<Dictionary<string, TransactionStatus>> TransactionStatuses() =>
        _db.BankTransactions.AsNoTracking().ToDictionaryAsync(t => t.ExternalId, t => t.Status);
}
