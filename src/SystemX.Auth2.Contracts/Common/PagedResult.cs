namespace SystemX.Auth2.Contracts.Common;

/// <summary>One page of a list.</summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Largest page size the server accepts.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
