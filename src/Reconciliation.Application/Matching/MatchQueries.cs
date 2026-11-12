using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Matching;

public record MatchResponse(
    Guid Id,
    Guid TransactionId,
    Guid InvoiceId,
    string InvoiceNumber,
    decimal Amount,
    int Score,
    string Reason,
    MatchMethod Method,
    string? CreatedBy,
    DateTimeOffset CreatedAt)
{
    /// <summary>Requires <see cref="Match.Invoice"/> to be loaded.</summary>
    public static MatchResponse From(Match match) => new(
        match.Id, match.TransactionId, match.InvoiceId, match.Invoice.Number, match.Amount, match.Score, match.Reason,
        match.Method, match.CreatedBy, match.CreatedAt);
}

public record MatchQuery : PageRequest
{
    public Guid? TransactionId { get; init; }
    public Guid? InvoiceId { get; init; }
    public MatchMethod? Method { get; init; }
}

public class MatchQueries(IApplicationDbContext db)
{
    public Task<PagedResult<MatchResponse>> ListAsync(MatchQuery query, CancellationToken ct)
    {
        var matches = db.Matches.AsNoTracking().Include(m => m.Invoice).AsQueryable();

        if (query.TransactionId is { } transactionId)
        {
            matches = matches.Where(m => m.TransactionId == transactionId);
        }

        if (query.InvoiceId is { } invoiceId)
        {
            matches = matches.Where(m => m.InvoiceId == invoiceId);
        }

        if (query.Method is { } method)
        {
            matches = matches.Where(m => m.Method == method);
        }

        return matches.OrderByDescending(m => m.CreatedAt).ToPagedResultAsync(query, MatchResponse.From, ct);
    }
}
