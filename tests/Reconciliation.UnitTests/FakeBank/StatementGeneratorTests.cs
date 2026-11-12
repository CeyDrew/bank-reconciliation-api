using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Reconciliation.Domain.ValueObjects;
using Reconciliation.FakeBank.Statements;

namespace Reconciliation.UnitTests.FakeBank;

public class StatementGeneratorTests
{
    private const string MainIban = "TR549999000000001000000001";
    private const string OtherIban = "TR279999000000001000000002";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly IOptions<StatementOptions> _options = Options.Create(new StatementOptions());
    private DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    [Fact]
    public void Same_time_gives_same_statement_even_from_a_new_instance()
    {
        // A restarted bank must not invent different transactions for the same period.
        var first = new StatementGenerator(_clock, _options).GetLines(OtherIban, Today.AddDays(-2));
        var second = new StatementGenerator(_clock, _options).GetLines(OtherIban, Today.AddDays(-2));

        first.Should().NotBeEmpty();
        second.Should().Equal(first);
    }

    [Fact]
    public void New_transactions_appear_as_time_passes_and_old_ones_stay()
    {
        var generator = new StatementGenerator(_clock, _options);
        var before = generator.GetLines(OtherIban, Today.AddDays(-2));

        _clock.Advance(TimeSpan.FromHours(3));
        var after = generator.GetLines(OtherIban, Today.AddDays(-2));

        after.Count.Should().BeGreaterThan(before.Count);
        after.Should().Contain(before.Where(l => l.BookingDate == Today));
    }

    [Fact]
    public void Main_account_contains_the_scripted_scenarios()
    {
        var lines = new StatementGenerator(_clock, _options).GetLines(MainIban, Today.AddDays(-30));

        lines.Where(l => l.TransactionId.StartsWith("FB-S")).Should().HaveCount(11)
            .And.Contain(l => l.TransactionId == "FB-S001" && l.Amount == 12_500.00m && l.Description.Contains("INV-2026-0001"));
    }

    [Fact]
    public void Other_accounts_have_no_scripted_scenarios()
    {
        new StatementGenerator(_clock, _options).GetLines(OtherIban, Today.AddDays(-30))
            .Should().NotContain(l => l.TransactionId.StartsWith("FB-S"));
    }

    [Fact]
    public void Counterparty_ibans_pass_the_checksum()
    {
        new StatementGenerator(_clock, _options).GetLines(MainIban, Today.AddDays(-30))
            .Where(l => l.CounterpartyIban is not null)
            .Should().OnlyContain(l => Iban.IsValid(l.CounterpartyIban));
    }

    [Fact]
    public void Pages_cover_every_line_exactly_once()
    {
        var generator = new StatementGenerator(_clock, _options);
        var all = generator.GetLines(MainIban, Today.AddDays(-30));

        var paged = new List<StatementLine>();
        StatementPage page;
        var number = 1;
        do
        {
            page = generator.GetPage(MainIban, Today.AddDays(-30), number++, 7);
            paged.AddRange(page.Items);
        } while (page.HasMore);

        paged.Should().Equal(all);
        all.Select(l => l.TransactionId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Lines_before_from_are_excluded()
    {
        new StatementGenerator(_clock, _options).GetLines(MainIban, Today)
            .Should().OnlyContain(l => l.BookingDate >= Today);
    }
}
