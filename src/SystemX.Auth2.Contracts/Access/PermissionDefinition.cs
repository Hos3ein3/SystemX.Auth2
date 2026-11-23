namespace SystemX.Auth2.Contracts.Access;

/// <summary>A permission, for example <c>orders.read</c>.</summary>
public sealed record PermissionDefinition(string Key, string? DisplayName = null, string? Description = null, string? Group = null);
