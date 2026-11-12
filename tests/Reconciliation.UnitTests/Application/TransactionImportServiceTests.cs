using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Transactions;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.ValueObjects;
using Reconciliation.Infrastructure.Locking;
using Reconciliation.Infrastructure.Persistence;

namespace Reconciliation.UnitTests.Application;

public class TransactionImportServiceTests
{
    private const string MainIban = "TR549999000000001000000001";
    private const string SecondIban = "TR279999000000001000000002";
    private static readonly DateOnly Today = new(2026, 11, 13);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly StubBank _bank = new();
    private readonly InMemoryDistributedLock _lock;
    private readonly TransactionImportOptions _options = new() { InitialLookbackDays = 30, OverlapDays = 2 };

    public TransactionImportServiceTests()
    {
        _lock = new InMemoryDistributedLock(_clock);
    }

    [Fact]
    public async Task First_run_imports_every_page_and_second_run_imports_nothing()
    {
        var main = AddAccount(MainIban);
        _bank.Add(MainIban, Line("T1", -5), Line("T2", -4), Line("T3", -3), Line("T4", -1), Line("T5", 0));

        var first = await CreateService().RunAsync(CancellationToken.None);
        var second = await CreateService().RunAsync(CancellationToken.None);

        first.Should().BeEquivalentTo(new ImportResult(1, 0, 5, 5, 0, 0));
        second!.Imported.Should().Be(0);
        second.Duplicates.Should().Be(second.Fetched);
        _db.BankTransactions.Where(t => t.AccountId == main.Id).Should().HaveCount(5);
    }

    [Fact]
    public async Task Later_run_starts_overlap_days_before_newest_booking()
    {
        AddAccount(MainIban);
        _bank.Add(MainIban, Line("T1", -10), Line("T2", -3));

        await CreateService().RunAsync(CancellationToken.None);
        await CreateService().RunAsync(CancellationToken.None);

        _bank.Calls.Select(c => c.From).Distinct().Should().Equal(Today.AddDays(-30), Today.AddDays(-3 - 2));
    }

    [Fact]
    public async Task Late_booked_transaction_inside_overlap_is_picked_up()
    {
        var main = AddAccount(MainIban);
        _bank.Add(MainIban, Line("T1", 0));
        await CreateService().RunAsync(CancellationToken.None);

        _bank.Add(MainIban, Line("LATE", -1)); // the bank books it a day back
        var result = await CreateService().RunAsync(CancellationToken.None);

        result!.Imported.Should().Be(1);
        _db.BankTransactions.Where(t => t.AccountId == main.Id).Select(t => t.ExternalId).Should().BeEquivalentTo(["T1", "LATE"]);
    }

    [Fact]
    public async Task Inactive_account_is_skipped()
    {
        AddAccount(MainIban).Deactivate();
        await _db.SaveChangesAsync();
        _bank.Add(MainIban, Line("T1", 0));

        var result = await CreateService().RunAsync(CancellationToken.None);

        result!.Accounts.Should().Be(0);
        _bank.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_lines_are_rejected_and_the_rest_imported()
    {
        AddAccount(MainIban);
        _bank.Add(MainIban, Line("OK", 0), Line("ZERO", 0, amount: 0m), Line("BADCCY", 0, currency: "tl"));

        var result = await CreateService().RunAsync(CancellationToken.None);

        result.Should().BeEquivalentTo(new ImportResult(1, 0, 3, 1, 0, 2));
        _db.BankTransactions.Select(t => t.ExternalId).Should().Equal("OK");
    }

    [Fact]
    public async Task Failing_account_does_not_stop_the_others()
    {
        AddAccount(MainIban);
        AddAccount(SecondIban);
        _bank.Failing.Add(MainIban);
        _bank.Add(SecondIban, Line("T1", 0));

        var result = await CreateService().RunAsync(CancellationToken.None);

        result.Should().BeEquivalentTo(new ImportResult(2, 1, 1, 1, 0, 0));
    }

    [Fact]
    public async Task Returns_null_while_another_run_holds_the_lock()
    {
        AddAccount(MainIban);
        await using var held = await _lock.TryAcquireAsync(TransactionImportService.LockName, TimeSpan.FromMinutes(1), CancellationToken.None);

        var result = await CreateService().RunAsync(CancellationToken.None);

        result.Should().BeNull();
        _bank.Calls.Should().BeEmpty();
    }

    private TransactionImportService CreateService() =>
        new(_db, _bank, _lock, _clock, Options.Create(_options), NullLogger<TransactionImportService>.Instance);

    private Account AddAccount(string iban)
    {
        var account = new Account(iban[^4..], Iban.Create(iban), "TRY", _clock.GetUtcNow());
        _db.Accounts.Add(account);
        _db.SaveChanges();
        return account;
    }

    private static BankStatementLine Line(string id, int dayOffset, decimal amount = 100m, string currency = "TRY") =>
        new(id, Today.AddDays(dayOffset), amount, currency, "Transfer", "ACME", null);

    /// <summary>Serves lines two per page, like a paging bank API.</summary>
    private sealed class StubBank : IBankClient
    {
        private const int PageSize = 2;
        private readonly Dictionary<string, List<BankStatementLine>> _lines = [];

        public HashSet<string> Failing { get; } = [];
        public List<(string Iban, DateOnly From, int Page)> Calls { get; } = [];

        public void Add(string iban, params BankStatementLine[] lines)
        {
            if (!_lines.TryGetValue(iban, out var list))
            {
                _lines[iban] = list = [];
            }

            list.AddRange(lines);
        }

        public Task<BankStatementPage> GetTransactionsAsync(string iban, DateOnly from, int page, CancellationToken ct)
        {
            Calls.Add((iban, from, page));
            if (Failing.Contains(iban))
            {
                throw new HttpRequestException("Bank is down");
            }

            var matching = _lines.GetValueOrDefault(iban, []).Where(l => l.BookingDate >= from).OrderBy(l => l.BookingDate).ToList();
            var items = matching.Skip((page - 1) * PageSize).Take(PageSize).ToList();
            return Task.FromResult(new BankStatementPage(items, page * PageSize < matching.Count));
        }
    }
}
