using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Matching;

public class ReconciliationOptions
{
    public const string SectionName = "Reconciliation";

    /// <summary>Proposals scoring at least this much are applied automatically; the rest wait for a person.</summary>
    public int AutoMatchThreshold { get; init; } = 80;

    public TimeSpan LockExpiry { get; init; } = TimeSpan.FromMinutes(10);
}

public record ReconciliationResult(int Processed, int Matched, int Unmatched, decimal MatchedAmount);

public record SuggestionResponse(Guid TransactionId, bool HasProposal, int Score, bool WouldAutoMatch, string Explanation,
    IReadOnlyList<MatchAllocation> Allocations);

/// <summary>Runs the matching engine over unmatched incoming transactions and stores confident matches.</summary>
public class ReconciliationService(
    IApplicationDbContext db,
    MatchingEngine engine,
    IDistributedLock distributedLock,
    TimeProvider clock,
    IOptions<ReconciliationOptions> options,
    ILogger<ReconciliationService> logger)
{
    public const string LockName = "locks:reconciliation";
    public const string SystemUser = "system";

    /// <summary>Returns null when another reconciliation run is in progress.</summary>
    public async Task<ReconciliationResult?> RunAsync(CancellationToken ct)
    {
        await using var handle = await distributedLock.TryAcquireAsync(LockName, options.Value.LockExpiry, ct);
        if (handle is null)
        {
            logger.LogInformation("Reconciliation skipped: another run holds the lock");
            return null;
        }

        // Partially matched transactions are left alone: their remainder is an overpayment or awaits a person.
        var transactions = await db.BankTransactions
            .Where(t => t.Status == TransactionStatus.Unmatched && t.Amount > 0)
            .OrderBy(t => t.BookingDate).ThenBy(t => t.ImportedAt)
            .ToListAsync(ct);
        if (transactions.Count == 0)
        {
            return new ReconciliationResult(0, 0, 0, 0m);
        }

        // Loaded once and tracked: each applied match lowers an invoice's open amount for the next transaction.
        // Fine for thousands of open invoices; beyond that, load candidates per transaction instead.
        var openInvoices = await LoadOpenInvoicesAsync(ct);
        var customers = await db.Customers.AsNoTracking().ToListAsync(ct);
        var invoicesById = openInvoices.ToDictionary(i => i.Id);
        var threshold = options.Value.AutoMatchThreshold;

        int matched = 0, unmatched = 0;
        var matchedAmount = 0m;
        foreach (var transaction in transactions)
        {
            var result = engine.Evaluate(transaction, openInvoices, customers);
            if (!result.IsConfident(threshold))
            {
                unmatched++;
                logger.LogDebug("Transaction {TransactionId} left unmatched (score {Score}): {Explanation}",
                    transaction.Id, result.Score, result.Explanation);
                continue;
            }

            foreach (var allocation in result.Allocations)
            {
                db.Matches.Add(Match.Create(transaction, invoicesById[allocation.InvoiceId], allocation.Amount,
                    allocation.Score, allocation.Reason, MatchMethod.Automatic, SystemUser, clock.GetUtcNow()));
                matchedAmount += allocation.Amount;
            }

            matched++;
        }

        // One save: matches, transaction and invoice updates are stored together or not at all.
        // If someone changed an invoice meanwhile, the row version check fails and the next run retries.
        await db.SaveChangesAsync(ct);

        var summary = new ReconciliationResult(transactions.Count, matched, unmatched, matchedAmount);
        logger.LogInformation("Reconciliation finished: {@Result}", summary);
        return summary;
    }

    /// <summary>What the engine would do with a transaction now. Nothing is stored.</summary>
    public async Task<SuggestionResponse> SuggestAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.BankTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new NotFoundException("Transaction", transactionId);
        var openInvoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.PartiallyPaid)
            .ToListAsync(ct);
        var customers = await db.Customers.AsNoTracking().ToListAsync(ct);

        var result = engine.Evaluate(transaction, openInvoices, customers);
        return new SuggestionResponse(transaction.Id, result.HasProposal, result.Score,
            result.IsConfident(options.Value.AutoMatchThreshold), result.Explanation, result.Allocations);
    }

    private Task<List<Invoice>> LoadOpenInvoicesAsync(CancellationToken ct) =>
        db.Invoices
            .Where(i => i.Status == InvoiceStatus.Open || i.Status == InvoiceStatus.PartiallyPaid)
            .ToListAsync(ct);
}
