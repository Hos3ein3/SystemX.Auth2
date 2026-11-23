namespace SystemX.Auth2.Contracts.Access;

/// <summary>Result of a sync call. Sync never deletes.</summary>
public sealed record SyncResponse(int Added, int Updated, int Unchanged, IReadOnlyList<string> AddedKeys);
