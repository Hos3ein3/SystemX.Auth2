namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Create an application in a project. The client id is <c>{projectkey}.{name}</c> unless one is given. It must be unique on the server.</summary>
public sealed record CreateApplicationRequest(string Name, string? ClientId = null,
    IReadOnlyList<string>? RedirectUris = null, IReadOnlyList<string>? PostLogoutRedirectUris = null, IReadOnlyList<string>? AllowedScopes = null);
