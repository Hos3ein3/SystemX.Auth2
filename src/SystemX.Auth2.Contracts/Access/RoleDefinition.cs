namespace SystemX.Auth2.Contracts.Access;

/// <summary>A role with an optional list of permission keys.</summary>
public sealed record RoleDefinition(string Name, string? DisplayName = null, string? Description = null, IReadOnlyList<string>? Permissions = null);
