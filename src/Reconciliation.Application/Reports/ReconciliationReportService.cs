using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Domain.Common;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Reports;

public record ReconciliationReportQuery
{
    public const int MaxDays = 92;

    /// <summary>First booking date, inclusive. Defaults to today.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last booking date, inclusive. Defaults to <see cref="From"/>.</summary>
    public DateOnly? To { get; init; }

    public Guid? AccountId { get; init; }

    /// <summary>Amounts in different currencies can't be added up, so a report covers one currency.</summary>
    public string Currency { get; init; } = Domain.Common.Currency.TurkishLira;
}

public class ReconciliationReportQueryValidator : AbstractValidator<ReconciliationReportQuery>
{
    public ReconciliationReportQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.To.Value.DayNumber - x.From.Value.DayNumber < ReconciliationReportQuery.MaxDays)
            .WithName("To")
            .WithMessage($"A report covers at most {ReconciliationReportQuery.MaxDays} days.");
        RuleFor(x => x.Currency).Must(Currency.IsValid).WithMessage("'Currency' must be a 3-letter upper-case currency code.");
    }
}

/// <summary>
/// One booking day. "Unmatched" is the difference still to explain: incoming money not yet matched to invoices,
/// including the unmatched remainder of overpayments. Ignored transactions are reported separately.
/// </summary>
public record DailyReconciliation(
    DateOnly Date,
    int IncomingCount,
    decimal IncomingAmount,
    decimal MatchedAmount,
    int UnmatchedCount,
    decimal UnmatchedAmount,
    int IgnoredCount,
    decimal IgnoredAmount,
    int OutgoingCount,
    decimal OutgoingAmount)
{
    public static DailyReconciliation Empty(DateOnly date) => new(date, 0, 0m, 0m, 0, 0m, 0, 0m, 0, 0m);
}

public record ReconciliationTotals(
    int IncomingCount,
    decimal IncomingAmount,
    decimal MatchedAmount,
    int UnmatchedCount,
    decimal UnmatchedAmount,
    int IgnoredCount,
    decimal IgnoredAmount,
    int OutgoingCount,
    decimal OutgoingAmount,
    /// <summary>Share of incoming money (excluding ignored) that is matched, 0-100.</summary>
    decimal MatchRatePercent);

/// <summary>Invoices as of now; overdue means due before the report's last day.</summary>
public record OpenInvoicesSummary(int Count, decimal OpenAmount, int OverdueCount, decimal OverdueAmount);

public record ReconciliationReport(
    DateOnly From,
    DateOnly To,
    Guid? AccountId,
    string Currency,
    ReconciliationTotals Totals,
    OpenInvoicesSummary OpenInvoices,
    IReadOnlyList<DailyReconciliation> Days);

public class ReconciliationReportService(IApplicationDbContext db, IValidator<ReconciliationReportQuery> validator, TimeProvider clock)
{
    public async Task<ReconciliationReport> GetAsync(ReconciliationReportQuery query, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(query, ct);
        var from = query.From ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var to = query.To ?? from;

        var transactions = db.BankTransactions.AsNoTracking()
            .Where(t => t.BookingDate >= from && t.BookingDate <= to && t.Currency == query.Currency);
        if (query.AccountId is { } accountId)
        {
            transactions = transactions.Where(t => t.AccountId == accountId);
        }

        // One GROUP BY query; the sums are computed by SQL Server, not in memory.
        var rows = await transactions
            .GroupBy(t => t.BookingDate)
            .Select(g => new DailyReconciliation(
                g.Key,
                g.Count(t => t.Amount > 0),
                g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                g.Where(t => t.Amount > 0).Sum(t => t.MatchedAmount),
                g.Count(t => t.Amount > 0 && t.Status != TransactionStatus.Ignored && t.MatchedAmount < t.Amount),
                g.Where(t => t.Amount > 0 && t.Status != TransactionStatus.Ignored).Sum(t => t.Amount - t.MatchedAmount),
                g.Count(t => t.Status == TransactionStatus.Ignored),
                g.Where(t => t.Status == TransactionStatus.Ignored).Sum(t => t.Amount),
                g.Count(t => t.Amount < 0),
                g.Where(t => t.Amount < 0).Sum(t => -t.Amount)))
            .ToDictionaryAsync(d => d.Date, ct);

        // Days without movements are listed too, so the report has no gaps.
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(offset => from.AddDays(offset))
            .Select(date => rows.GetValueOrDefault(date) ?? DailyReconciliation.Empty(date))
            .ToList();

        return new ReconciliationReport(from, to, query.AccountId, query.Currency, Totals(days),
            await OpenInvoicesAsync(query.Currency, to, ct), days);
    }

    private static ReconciliationTotals Totals(IReadOnlyList<DailyReconciliation> days)
    {
        var incoming = days.Sum(d => d.IncomingAmount);
        var matched = days.Sum(d => d.MatchedAmount);
        var ignoredIncoming = Math.Max(days.Sum(d => d.IgnoredAmount), 0m);
        var reconcilable = incoming - ignoredIncoming;

        return new ReconciliationTotals(
            days.Sum(d => d.IncomingCount), incoming, matched,
            days.Sum(d => d.UnmatchedCount), days.Sum(d => d.UnmatchedAmount),
            days.Sum(d => d.IgnoredCount), days.Sum(d => d.IgnoredAmount),
            days.Sum(d => d.OutgoingCount), days.Sum(d => d.OutgoingAmount),
            reconcilable > 0 ? Math.Round(matched / reconcilable * 100, 1) : 0m);
    }

    private async Task<OpenInvoicesSummary> OpenInvoicesAsync(string currency, DateOnly asOf, CancellationToken ct)
    {
        var open = db.Invoices.AsNoTracking()
            .Where(i => i.Currency == currency && (i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.PartiallyPaid));

        var summary = await open
            .GroupBy(_ => 1)
            .Select(g => new OpenInvoicesSummary(
                g.Count(),
                g.Sum(i => i.Amount - i.PaidAmount),
                g.Count(i => i.DueDate < asOf),
                g.Where(i => i.DueDate < asOf).Sum(i => i.Amount - i.PaidAmount)))
            .FirstOrDefaultAsync(ct);

        return summary ?? new OpenInvoicesSummary(0, 0m, 0, 0m);
    }
}
