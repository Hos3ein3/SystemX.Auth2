namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Update an application. Null lists stay as they are. Disabling blocks only this application.</summary>
public sealed record UpdateApplicationRequest(string Name, bool IsEnabled,
    IReadOnlyList<string>? RedirectUris = null, IReadOnlyList<string>? PostLogoutRedirectUris = null, IReadOnlyList<string>? AllowedScopes = null, string? Reason = null);
