namespace SystemX.Auth2.Contracts.Access;

/// <summary>Request about groups. Groups are pull-only, so this only asks the server for names.</summary>
public sealed record GroupSyncRequest(IReadOnlyList<string>? Names = null);
