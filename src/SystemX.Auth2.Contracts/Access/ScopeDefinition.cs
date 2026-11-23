namespace SystemX.Auth2.Contracts.Access;

/// <summary>A custom scope of an application, for example <c>reports.read</c>. Names starting with <c>auth2.</c> and the standard OpenID Connect scopes are reserved.</summary>
public sealed record ScopeDefinition(string Name, string? DisplayName = null, string? Description = null);

/// <summary>Scopes sent by a client (push).</summary>
public sealed record ScopeSyncRequest(IReadOnlyList<ScopeDefinition> Scopes);

/// <summary>A custom scope as the server knows it.</summary>
public sealed record ScopeDto(string Name, string? DisplayName, string? Description);
