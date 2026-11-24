namespace SystemX.Auth2.Server.Domain.Entities;

/// <summary>Audit record. Never store secrets in it.</summary>
public class AuditEvent
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }
    /// <summary>Project id, or null for server-wide events.</summary>
    public Guid? ProjectId { get; set; }
    /// <summary>The application (client) that did it, when the request had one.</summary>
    public Guid? ApplicationId { get; set; }
    /// <summary>Acting user.</summary>
    public Guid? ActorUserId { get; set; }
    /// <summary>Acting client.</summary>
    public string? ActorClientId { get; set; }
    /// <summary>Action name, for example <c>seed.global_admin_created</c>.</summary>
    public string Action { get; set; } = "";
    /// <summary>Target type.</summary>
    public string? TargetType { get; set; }
    /// <summary>Target id.</summary>
    public string? TargetId { get; set; }
    /// <summary>Caller IP address.</summary>
    public string? IpAddress { get; set; }
    /// <summary>Caller user agent.</summary>
    public string? UserAgent { get; set; }
    /// <summary>Extra data as JSON (jsonb).</summary>
    public string? DataJson { get; set; }
}

/// <summary>One-time code. The database table is used when Redis is off.</summary>
public class OtpCode
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>User id.</summary>
    public Guid UserId { get; set; }
    /// <summary>Application id.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>Purpose, for example <c>login</c>.</summary>
    public string Purpose { get; set; } = "";
    /// <summary>Hash of the code.</summary>
    public string CodeHash { get; set; } = "";
    /// <summary>Expiry.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
    /// <summary>Wrong attempts so far.</summary>
    public int Attempts { get; set; }
    /// <summary>When it was used.</summary>
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}

/// <summary>What the mapping reads from the external source.</summary>
public enum MappingSourceType
{
    /// <summary>A group name of the source.</summary>
    Group = 0,
    /// <summary>A claim value (OIDC).</summary>
    Claim = 1,
    /// <summary>An attribute value (SAML, LDAP).</summary>
    Attribute = 2,
}
/// <summary>How the value is compared. No regex: only exact and prefix, case-insensitive.</summary>
public enum MappingMatchType
{
    /// <summary>Same text (ignoring case).</summary>
    Exact = 0,
    /// <summary>Starts with the text (ignoring case).</summary>
    Prefix = 1,
}
/// <summary>What the mapping gives.</summary>
public enum MappingTargetType
{
    /// <summary>A role.</summary>
    Role = 0,
    /// <summary>A group.</summary>
    Group = 1,
    /// <summary>A direct permission.</summary>
    Permission = 2,
}

/// <summary>One rule that turns a group, claim or attribute of an external provider into a role, group or permission of the same application.</summary>
public class ExternalGroupMapping
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Application. A mapping can only give things of this application.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>Provider name. Null means all providers of the application.</summary>
    public string? ProviderName { get; set; }
    /// <summary>Source kind.</summary>
    public MappingSourceType SourceType { get; set; }
    /// <summary>Claim or attribute name. Empty for groups.</summary>
    public string SourceName { get; set; } = "";
    /// <summary>Comparison.</summary>
    public MappingMatchType MatchType { get; set; }
    /// <summary>Value to compare.</summary>
    public string SourceValue { get; set; } = "";
    /// <summary>Kind of target.</summary>
    public MappingTargetType TargetType { get; set; }
    /// <summary>Id of the role, group or permission.</summary>
    public Guid TargetId { get; set; }
}

/// <summary>
/// A value that may be used only once until it expires: a SAML request id, an assertion id or a login state nonce.
/// Stored in the database so that all server instances see it.
/// </summary>
public class ReplayEntry
{
    /// <summary>Kind and value, for example <c>saml-assertion:_abc</c>. Unique.</summary>
    public string Key { get; set; } = "";
    /// <summary>Application.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>Remove after this time.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

/// <summary>A server-wide value that an admin may change at run time (for example the blocked passkey models).</summary>
public class ServerValue
{
    /// <summary>Name of the value.</summary>
    public string Key { get; set; } = "";
    /// <summary>The value as text.</summary>
    public string Value { get; set; } = "";
    /// <summary>Last change.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>History record of a maintenance job run.</summary>
public class JobRun
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Job name, e.g. <c>otp-cleanup</c>.</summary>
    public string JobName { get; set; } = "";
    /// <summary>Server instance id.</summary>
    public string? InstanceId { get; set; }
    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }
    /// <summary>When the run finished.</summary>
    public DateTimeOffset? FinishedAtUtc { get; set; }
    /// <summary>True if successful.</summary>
    public bool Success { get; set; }
    /// <summary>Short error description if failed.</summary>
    public string? Error { get; set; }
    /// <summary>Number of rows affected.</summary>
    public int RowsAffected { get; set; }
}

/// <summary>Branding asset (logo, favicon, background) stored in PostgreSQL.</summary>
public class BrandingAsset
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Application id.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>Asset kind: <c>logo</c>, <c>logo-dark</c>, <c>favicon</c>, <c>background</c>.</summary>
    public string Kind { get; set; } = "";
    /// <summary>MIME content type, e.g. <c>image/png</c>.</summary>
    public string ContentType { get; set; } = "";
    /// <summary>Binary data.</summary>
    public byte[] Data { get; set; } = [];
    /// <summary>SHA-256 hash of the binary data.</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>Size in bytes.</summary>
    public long Size { get; set; }
    /// <summary>Last update time.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Custom email template per application and culture.</summary>
public class EmailTemplate
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Application id.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>Template name, e.g. <c>verification</c>, <c>password-reset</c>.</summary>
    public string Name { get; set; } = "";
    /// <summary>Culture code, e.g. <c>en</c>.</summary>
    public string Culture { get; set; } = "en";
    /// <summary>Subject line.</summary>
    public string Subject { get; set; } = "";
    /// <summary>Body content.</summary>
    public string Body { get; set; } = "";
    /// <summary>Last update time.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

