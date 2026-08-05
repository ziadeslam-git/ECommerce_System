namespace ECommerce_System.Common;

/// <summary>
/// Wraps a paged query result — items for the current page + total count for pagination UI.
/// </summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items      { get; init; } = [];
    public int              TotalCount { get; init; }
    public int              Page       { get; init; }
    public int              PageSize   { get; init; }

    public int  TotalPages  => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPrevious => Page > 1;
    public bool HasNext     => Page < TotalPages;

    public static PagedResult<T> Empty(int page = 1, int pageSize = 10)
        => new() { Items = [], TotalCount = 0, Page = page, PageSize = pageSize };

    public static PagedResult<T> From(IEnumerable<T> items, int totalCount, int page, int pageSize)
        => new() { Items = items.ToList(), TotalCount = totalCount, Page = page, PageSize = pageSize };
}
