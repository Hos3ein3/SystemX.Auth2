using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Domain.Entities;

/// <summary>An application (project). One row per project. <c>_global</c> is the built-in admin project.</summary>
public class Auth2Application
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Unique project key. Ignored by logic in single-application mode.</summary>
    public string ProjectKey { get; set; } = "";
    /// <summary>Name. The client handshake checks it.</summary>
    public string Name { get; set; } = "";
    /// <summary>Description.</summary>
    public string? Description { get; set; }
    /// <summary>OAuth client id of the matching OpenIddict application (unique).</summary>
    public string ClientId { get; set; } = "";
    /// <summary>Id of the OpenIddict application row.</summary>
    public Guid? OpenIddictApplicationId { get; set; }
    /// <summary>False disables logins and API calls.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>Settings (stored as jsonb).</summary>
    public ApplicationSettings Settings { get; set; } = new();
    /// <summary>Raised on every settings change.</summary>
    public int SettingsVersion { get; set; } = 1;
    /// <summary>Raised on every change that affects access.</summary>
    public int AccessVersion { get; set; } = 1;
    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>True for the built-in <c>_global</c> project.</summary>
    public bool IsGlobalProject => ProjectKey == ProjectKeyRules.GlobalProjectKey;
}

/// <summary>Status of a user inside one application.</summary>
public enum UserApplicationStatus
{
    /// <summary>Normal.</summary>
    Active = 0,
    /// <summary>Blocked in this application only.</summary>
    Suspended = 1,
}

/// <summary>"This person belongs to this application". Also holds per-application credentials. Not used in single mode.</summary>
public class UserApplication
{
    /// <summary>User id.</summary>
    public Guid UserId { get; set; }
    /// <summary>Application id.</summary>
    public Guid ApplicationId { get; set; }
    /// <summary>When the user joined.</summary>
    public DateTimeOffset JoinedAtUtc { get; set; }
    /// <summary>Status.</summary>
    public UserApplicationStatus Status { get; set; }
    /// <summary>Password hash for this application.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Security stamp for this application.</summary>
    public string? SecurityStamp { get; set; }
    /// <summary>Is TOTP on.</summary>
    public bool TwoFactorEnabled { get; set; }
    /// <summary>TOTP key. Encrypted with Data Protection.</summary>
    public string? AuthenticatorKey { get; set; }
    /// <summary>Failed login count.</summary>
    public int AccessFailedCount { get; set; }
    /// <summary>End of lockout.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }
    /// <summary>Can this user be locked.</summary>
    public bool LockoutEnabled { get; set; } = true;
    /// <summary>Raised on changes that affect this user's access.</summary>
    public int UserAccessVersion { get; set; } = 1;
    /// <summary>Last login.</summary>
    public DateTimeOffset? LastLoginUtc { get; set; }
    /// <summary>Current session id when <c>SingleActiveSession</c> is on.</summary>
    public string? ActiveSessionId { get; set; }
}

/// <summary>Hashed recovery code for TOTP.</summary>
public class UserApplicationRecoveryCode
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>User id.</summary>
    public Guid UserId { get; set; }
    /// <summary>Application id.</summary>
    public Guid ApplicationId { get; set; }
    /// <summary>Hash of the code.</summary>
    public string CodeHash { get; set; } = "";
    /// <summary>When the code was used.</summary>
    public DateTimeOffset? UsedAtUtc { get; set; }
}
