using Microsoft.EntityFrameworkCore;

namespace Reconciliation.Application.Common;

/// <summary>Paging parameters bound from the query string. Out-of-range values are clamped.</summary>
public record PageRequest
{
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    internal int SafePage => Math.Max(Page, 1);
    internal int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    /// <summary>Runs a count and a page query, then maps the loaded entities to response objects.</summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TEntity, TResult>(
        this IQueryable<TEntity> query, PageRequest request, Func<TEntity, TResult> map, CancellationToken cancellationToken)
    {
        var page = request.SafePage;
        var pageSize = request.SafePageSize;

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items.Select(map).ToList(), page, pageSize, total);
    }
}
