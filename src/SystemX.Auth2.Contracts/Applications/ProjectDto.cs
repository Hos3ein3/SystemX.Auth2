namespace SystemX.Auth2.Contracts.Applications;

/// <summary>A project as shown in the admin API.</summary>
public sealed record ProjectDto(Guid Id, string? ProjectKey, string Name, string? Description,
    bool IsEnabled, int SettingsVersion, DateTimeOffset CreatedAtUtc);
