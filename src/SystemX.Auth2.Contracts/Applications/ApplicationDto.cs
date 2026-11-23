namespace SystemX.Auth2.Contracts.Applications;

/// <summary>An application (project) as shown in the admin API.</summary>
public sealed record ApplicationDto(Guid Id, string? ProjectKey, string Name, string? Description, string ClientId,
    bool IsEnabled, int SettingsVersion, DateTimeOffset CreatedAtUtc);
