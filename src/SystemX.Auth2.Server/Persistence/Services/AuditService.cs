using System.Text.Json;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Persistence.Services;

/// <summary>Writes audit events to the database. Callers must never pass secrets.</summary>
public sealed class AuditService(Auth2DbContext db, TimeProvider clock) : IAuditService
{
    /// <inheritdoc />
    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAtUtc = clock.GetUtcNow(),
            ApplicationId = entry.ApplicationId,
            ActorUserId = entry.ActorUserId,
            ActorClientId = entry.ActorClientId,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            IpAddress = entry.IpAddress,
            UserAgent = entry.UserAgent,
            DataJson = entry.Data is null ? null : JsonSerializer.Serialize(entry.Data),
        });
        await db.SaveChangesAsync(ct);
    }
}
