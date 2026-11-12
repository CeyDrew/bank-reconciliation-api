using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reconciliation.Application.Abstractions;
using Reconciliation.Domain.Common;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Application.Transactions;

public class TransactionImportOptions
{
    public const string SectionName = "TransactionImport";

    /// <summary>How far back the first import of an account reaches.</summary>
    public int InitialLookbackDays { get; init; } = 30;

    /// <summary>
    /// Later imports start this many days before the newest booking date already stored,
    /// so movements the bank books late are still picked up. Duplicates are skipped.
    /// </summary>
    public int OverlapDays { get; init; } = 2;

    /// <summary>Safety stop against a bank that keeps reporting more pages.</summary>
    public int MaxPagesPerAccount { get; init; } = 100;

    public TimeSpan LockExpiry { get; init; } = TimeSpan.FromMinutes(10);
}

public record ImportResult(int Accounts, int FailedAccounts, int Fetched, int Imported, int Duplicates, int Rejected)
{
    public static readonly ImportResult Empty = new(0, 0, 0, 0, 0, 0);

    public static ImportResult operator +(ImportResult a, ImportResult b) => new(
        a.Accounts + b.Accounts, a.FailedAccounts + b.FailedAccounts, a.Fetched + b.Fetched,
        a.Imported + b.Imported, a.Duplicates + b.Duplicates, a.Rejected + b.Rejected);
}

/// <summary>
/// Copies new movements of every active account from the bank into the database.
/// Safe to run again at any time: a movement is identified by (account, bank transaction id)
/// and never stored twice.
/// </summary>
public class TransactionImportService(
    IApplicationDbContext db,
    IBankClient bank,
    IDistributedLock distributedLock,
    TimeProvider clock,
    IOptions<TransactionImportOptions> options,
    ILogger<TransactionImportService> logger)
{
    public const string LockName = "locks:transaction-import";

    /// <summary>Runs one import. Returns null when another run (on any instance) is already in progress.</summary>
    public async Task<ImportResult?> RunAsync(CancellationToken ct)
    {
        await using var handle = await distributedLock.TryAcquireAsync(LockName, options.Value.LockExpiry, ct);
        if (handle is null)
        {
            logger.LogInformation("Transaction import skipped: another run holds the lock");
            return null;
        }

        var accounts = await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(ct);
        var total = ImportResult.Empty;

        foreach (var account in accounts)
        {
            try
            {
                total += await ImportAccountAsync(account, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreachable account must not stop the others; the next run retries it.
                logger.LogError(ex, "Transaction import failed for account {AccountId}", account.Id);
                db.ChangeTracker.Clear(); // drop unsaved rows so they don't fail the next account's save too
                total += ImportResult.Empty with { Accounts = 1, FailedAccounts = 1 };
            }
        }

        logger.LogInformation("Transaction import finished: {@Result}", total);
        return total;
    }

    private async Task<ImportResult> ImportAccountAsync(Account account, CancellationToken ct)
    {
        var settings = options.Value;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var newestBooking = await db.BankTransactions
            .Where(t => t.AccountId == account.Id)
            .MaxAsync(t => (DateOnly?)t.BookingDate, ct);
        var from = newestBooking?.AddDays(-settings.OverlapDays) ?? today.AddDays(-settings.InitialLookbackDays);

        var knownIds = (await db.BankTransactions
                .Where(t => t.AccountId == account.Id && t.BookingDate >= from)
                .Select(t => t.ExternalId)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        int fetched = 0, imported = 0, duplicates = 0, rejected = 0;
        var page = 1;
        BankStatementPage statement;
        do
        {
            statement = await bank.GetTransactionsAsync(account.Iban.Value, from, page, ct);
            foreach (var line in statement.Items)
            {
                fetched++;
                if (!knownIds.Add(line.TransactionId))
                {
                    duplicates++;
                    continue;
                }

                try
                {
                    db.BankTransactions.Add(new BankTransaction(account.Id, line.TransactionId, line.BookingDate, line.Amount,
                        line.Currency, line.Description, line.CounterpartyName, line.CounterpartyIban, clock.GetUtcNow()));
                    imported++;
                }
                catch (DomainException ex)
                {
                    rejected++;
                    logger.LogWarning("Rejected bank transaction {TransactionId} on account {AccountId}: {Reason}",
                        line.TransactionId, account.Id, ex.Message);
                }
            }

            // Save page by page: if a later page fails, the next run continues from what is stored.
            await db.SaveChangesAsync(ct);
            page++;
        } while (statement.HasMore && page <= settings.MaxPagesPerAccount);

        logger.LogInformation("Account {AccountId}: fetched {Fetched}, imported {Imported}, duplicates {Duplicates}, rejected {Rejected}",
            account.Id, fetched, imported, duplicates, rejected);

        return new ImportResult(1, 0, fetched, imported, duplicates, rejected);
    }
}
