using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Domain.Entities;

/// <summary>
/// A project: the tenant and product boundary (for example PaperTrade). Users, roles, permissions, groups and credentials belong to a project.
/// Its clients are <c>Auth2Project</c> rows. <c>_global</c> is the built-in admin project.
/// </summary>
public class Project
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Unique project key (lowercase letters, digits, hyphens). It never changes after creation. Not a secret.</summary>
    public string ProjectKey { get; set; } = "";
    /// <summary>Name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Description.</summary>
    public string? Description { get; set; }
    /// <summary>False blocks every login and token of the whole project.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>Project settings (stored as jsonb).</summary>
    public ProjectSettings Settings { get; set; } = new();
    /// <summary>Raised on every settings change.</summary>
    public int SettingsVersion { get; set; } = 1;
    /// <summary>Raised on every change that affects access.</summary>
    public int AccessVersion { get; set; } = 1;
    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>Last change.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
    /// <summary>Database concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    /// <summary>True for the built-in <c>_global</c> project.</summary>
    public bool IsGlobalProject => ProjectKey == ProjectKeyRules.GlobalProjectKey;
}

/// <summary>Status of a user inside one project.</summary>
public enum UserProjectStatus
{
    /// <summary>Normal.</summary>
    Active = 0,
    /// <summary>Blocked in this project only.</summary>
    Suspended = 1,
}

/// <summary>"This person belongs to this project". Also holds per-project credentials. Not used in single mode.</summary>
public class UserProject
{
    /// <summary>User id.</summary>
    public Guid UserId { get; set; }
    /// <summary>Project id.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>When the user joined.</summary>
    public DateTimeOffset JoinedAtUtc { get; set; }
    /// <summary>Status.</summary>
    public UserProjectStatus Status { get; set; }
    /// <summary>Password hash for this project.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Security stamp for this project.</summary>
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
public class UserProjectRecoveryCode
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>User id.</summary>
    public Guid UserId { get; set; }
    /// <summary>Project id.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>Hash of the code.</summary>
    public string CodeHash { get; set; } = "";
    /// <summary>When the code was used.</summary>
    public DateTimeOffset? UsedAtUtc { get; set; }
}
