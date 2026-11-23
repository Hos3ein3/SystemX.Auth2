using System.Text.Json;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Persistence.Services;

/// <summary>Writes audit events to the database. Callers must never pass secrets.</summary>
public sealed class AuditService(Auth2DbContext db, TimeProvider clock, IHttpContextAccessor http) : IAuditService
{
    /// <summary>Key in <c>HttpContext.Items</c> that holds the id of the admin making the call.</summary>
    public const string ActorItemKey = "Auth2.ActorUserId";

    private static string? Trim(string? v) => string.IsNullOrEmpty(v) ? null : v.Length > 256 ? v[..256] : v;

    /// <inheritdoc />
    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        // Inside a web request the caller's address and agent are added, and the admin who made the call (set by the admin API filter).
        var context = http.HttpContext;
        var actor = entry.ActorUserId ?? (context?.Items.TryGetValue(ActorItemKey, out var a) == true ? a as Guid? : null);
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAtUtc = clock.GetUtcNow(),
            ApplicationId = entry.ApplicationId,
            ActorUserId = actor,
            ActorClientId = entry.ActorClientId,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            IpAddress = entry.IpAddress ?? context?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = entry.UserAgent ?? Trim(context?.Request.Headers.UserAgent.ToString()),
            DataJson = entry.Data is null ? null : JsonSerializer.Serialize(entry.Data),
        });
        await db.SaveChangesAsync(ct);
    }
}
