namespace SystemX.Auth2.Contracts.Common;

/// <summary>Error body returned by the server API. Compatible with problem details.</summary>
/// <param name="Code">Stable machine-readable code. See <c>Auth2ErrorCodes</c>.</param>
/// <param name="Message">Localized human-readable message.</param>
/// <param name="Details">Optional extra data, for example field errors.</param>
public sealed record ApiError(string Code, string? Message = null, IReadOnlyDictionary<string, string[]>? Details = null);
