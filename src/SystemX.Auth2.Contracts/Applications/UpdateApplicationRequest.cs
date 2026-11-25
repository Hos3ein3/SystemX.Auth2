namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Update an application. Null lists stay as they are. Disabling blocks only this application. <c>ApplicationType</c> and <c>WebServerUrl</c> are changed by a global admin only; null leaves them, an empty <c>WebServerUrl</c> clears it.</summary>
public sealed record UpdateApplicationRequest(string Name, bool IsEnabled,
    IReadOnlyList<string>? RedirectUris = null, IReadOnlyList<string>? PostLogoutRedirectUris = null, IReadOnlyList<string>? AllowedScopes = null, string? Reason = null,
    Enums.ApplicationType? ApplicationType = null, string? WebServerUrl = null);
