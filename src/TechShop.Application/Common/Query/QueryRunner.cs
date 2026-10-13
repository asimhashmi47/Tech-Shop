using Microsoft.EntityFrameworkCore;

namespace TechShop.Application.Common.Query;

/// <summary>
/// Runs a <see cref="QueryRequest"/> against a projected DTO queryable. Projecting first
/// means the grid can sort and filter on flattened columns such as ShopName, while the
/// whitelist in <see cref="QuerySpec{T}"/> still decides what is reachable.
/// </summary>
public static class QueryRunner
{
    public const int MaxExportRows = 50_000;

    public static async Task<PagedResult<T>> RunAsync<T>(
        IQueryable<T> source,
        QueryRequest request,
        QuerySpec<T> spec,
        CancellationToken ct = default)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 500 ? 25 : request.PageSize;

        var filtered = source.ApplyFilters(request, spec);
        var total = await filtered.CountAsync(ct).ConfigureAwait(false);
        var items = await filtered
            .ApplySort(request, spec)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    /// <summary>
    /// Same contract for small sets that were already materialised, such as the user list,
    /// whose Shops column is built in memory because SQLite cannot concatenate rows.
    /// </summary>
    public static PagedResult<T> RunInMemory<T>(IEnumerable<T> source, QueryRequest request, QuerySpec<T> spec)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 500 ? 25 : request.PageSize;

        var filtered = source.AsQueryable().ApplyFilters(request, spec);

        return new PagedResult<T>
        {
            Items = filtered.ApplySort(request, spec).Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = filtered.Count()
        };
    }

    public static List<T> RunInMemoryForExport<T>(IEnumerable<T> source, QueryRequest request, QuerySpec<T> spec)
        => source.AsQueryable().ApplyQuery(request, spec).Take(MaxExportRows).ToList();

    /// <summary>Loads the full filtered and sorted set for Excel export, capped for safety.</summary>
    public static Task<List<T>> RunForExportAsync<T>(
        IQueryable<T> source,
        QueryRequest request,
        QuerySpec<T> spec,
        CancellationToken ct = default)
        => source
            .ApplyQuery(request, spec)
            .Take(MaxExportRows)
            .ToListAsync(ct);
}

