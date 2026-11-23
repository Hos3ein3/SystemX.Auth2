namespace SystemX.Auth2.Contracts.Access;

/// <summary>Request to upsert roles.</summary>
public sealed record RoleSyncRequest(IReadOnlyList<RoleDefinition> Roles);
