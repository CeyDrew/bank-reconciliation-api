using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Common;

namespace Reconciliation.Application.Transactions;

/// <summary>Read side for bank transactions. Transactions are created by the import job, never through the API.</summary>
public class TransactionService(IApplicationDbContext db)
{
    public Task<PagedResult<TransactionResponse>> ListAsync(TransactionQuery query, CancellationToken ct)
    {
        var transactions = db.BankTransactions.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            transactions = transactions.Where(t => t.AccountId == accountId);
        }

        if (query.Status is { } status)
        {
            transactions = transactions.Where(t => t.Status == status);
        }

        if (query.From is { } from)
        {
            transactions = transactions.Where(t => t.BookingDate >= from);
        }

        if (query.To is { } to)
        {
            transactions = transactions.Where(t => t.BookingDate <= to);
        }

        return transactions
            .OrderByDescending(t => t.BookingDate).ThenByDescending(t => t.ImportedAt)
            .ToPagedResultAsync(query, TransactionResponse.From, ct);
    }

    public async Task<TransactionResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var transaction = await db.BankTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Transaction", id);
        return TransactionResponse.From(transaction);
    }
}
