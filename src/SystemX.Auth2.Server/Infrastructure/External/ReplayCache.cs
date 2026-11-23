using Microsoft.EntityFrameworkCore;
using Npgsql;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <inheritdoc />
public sealed class DatabaseReplayCache(IServiceScopeFactory scopes, TimeProvider clock) : IReplayCache
{
    /// <inheritdoc />
    public async Task<bool> TryAddAsync(Guid applicationId, string key, DateTimeOffset expiresAtUtc, CancellationToken ct = default)
    {
        // A separate scope and context: a unique-key failure must not poison the caller's context.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var now = clock.GetUtcNow();
        // An old entry with the same key that already expired must not block a new one.
        await db.ReplayEntries.Where(r => r.Key == key && r.ExpiresAtUtc < now).ExecuteDeleteAsync(ct);
        db.ReplayEntries.Add(new ReplayEntry { Key = key.Length > 400 ? key[..400] : key, ApplicationId = applicationId, ExpiresAtUtc = expiresAtUtc });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }
}
