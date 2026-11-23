namespace SystemX.Auth2.Contracts.Access;

/// <summary>
/// Groups sent by a client (push). <c>Groups</c> carries names and display names. <c>Names</c> is the short form (names only).
/// When both are set, <c>Groups</c> wins. The server only adds groups that are missing.
/// </summary>
public sealed record GroupSyncRequest(IReadOnlyList<string>? Names = null, IReadOnlyList<GroupDefinition>? Groups = null);
