using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Persistence.Seeding;

namespace SystemX.Auth2.Server.Persistence;

/// <summary>Runs migrations and the seeder. A PostgreSQL advisory lock keeps several instances from doing it at the same time.</summary>
public sealed class DatabaseInitializer(Auth2DbContext db, Auth2Seeder seeder, SystemX.Auth2.Server.Application.IKeyRing keyRing, ILogger<DatabaseInitializer> log)
{
    private const long LockKey = 0x5359_5358_4155_5432; // "SYSXAUT2"

    /// <summary>Applies migrations when <paramref name="migrate"/> is true and seeds when <paramref name="seed"/> is true.</summary>
    public async Task RunAsync(bool migrate, bool seed, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({LockKey})", ct);
            try
            {
                if (migrate)
                {
                    log.LogInformation("Applying database migrations.");
                    await db.Database.MigrateAsync(ct);
                }
                if (seed)
                {
                    log.LogInformation("Seeding.");
                    await seeder.SeedAsync(ct);
                }
            }
            finally
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({LockKey})", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
        // The first signing and encryption keys. (Its own lock; does nothing when keys exist or certificates are configured and unused.)
        if (migrate || seed) await keyRing.EnsureAsync(ct);
    }
}
