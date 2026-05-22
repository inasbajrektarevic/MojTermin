using Microsoft.EntityFrameworkCore;

namespace MojTermin.Api.Application;

/// <summary>
/// Tiny pagination helper used by admin list endpoints. Keeps the response shape
/// (a flat List&lt;T&gt;) so existing frontend code is not broken, but bounds the
/// number of rows so a long-lived tenant cannot DoS the API by pulling 10k rows.
/// </summary>
public static class PaginationExtensions
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static IQueryable<T> ApplyPagination<T>(
        this IQueryable<T> query,
        int? page,
        int? pageSize)
    {
        var resolvedPage = page is null or < 1 ? 1 : page.Value;
        var resolvedPageSize = pageSize switch
        {
            null => DefaultPageSize,
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value
        };

        return query
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize);
    }

    public static Task<List<T>> ToPagedListAsync<T>(
        this IQueryable<T> query,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
        => query.ApplyPagination(page, pageSize).ToListAsync(cancellationToken);
}
