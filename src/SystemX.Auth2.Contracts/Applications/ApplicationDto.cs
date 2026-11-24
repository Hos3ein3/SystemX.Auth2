namespace SystemX.Auth2.Contracts.Applications;

/// <summary>An application (a client of a project) as shown in the admin API. The secret is never part of it.</summary>
public sealed record ApplicationDto(Guid Id, Guid ProjectId, string Name, string ClientId, bool IsEnabled,
    IReadOnlyList<string> RedirectUris, IReadOnlyList<string> PostLogoutRedirectUris, IReadOnlyList<string> AllowedScopes,
    int SettingsVersion, DateTimeOffset CreatedAtUtc);
