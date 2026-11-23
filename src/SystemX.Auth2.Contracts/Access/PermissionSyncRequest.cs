namespace SystemX.Auth2.Contracts.Access;

/// <summary>Request to upsert permissions.</summary>
public sealed record PermissionSyncRequest(IReadOnlyList<PermissionDefinition> Permissions);
