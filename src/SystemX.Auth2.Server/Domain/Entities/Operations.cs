namespace SystemX.Auth2.Server.Domain.Entities;

/// <summary>Audit record. Never store secrets in it.</summary>
public class AuditEvent
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }
    /// <summary>Application id, or null for server-wide events.</summary>
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
    public Guid ApplicationId { get; set; }
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
