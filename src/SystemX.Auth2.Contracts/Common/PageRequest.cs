namespace SystemX.Auth2.Contracts.Common;

/// <summary>Which page of a list to read. The server never returns more than <see cref="PagedResult{T}.MaxPageSize"/> items.</summary>
/// <param name="Page">First page is 1.</param>
/// <param name="PageSize">Items per page.</param>
/// <param name="Search">Part of the name (or key) to look for; <c>null</c> for no filter.</param>
/// <param name="Descending">Sort Z to A.</param>
public sealed record PageRequest(int Page = 1, int PageSize = 50, string? Search = null, bool Descending = false);
