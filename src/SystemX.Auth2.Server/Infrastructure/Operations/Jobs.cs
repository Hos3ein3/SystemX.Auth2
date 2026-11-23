using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenIddict.Abstractions;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>A lock held by one database session (PostgreSQL advisory lock). Works across instances without Redis.</summary>
public sealed class PostgresDistributedLock(IServiceScopeFactory scopes) : IDistributedLock
{
    /// <inheritdoc />
    public async Task<bool> TryRunAsync(string name, Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        string connectionString;
        await using (var scope = scopes.CreateAsyncScope())
            connectionString = scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Database.GetConnectionString()!;

        // A dedicated connection holds the lock, so the work can use its own connections freely.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@name, 0))", conn))
        {
            cmd.Parameters.AddWithValue("name", "auth2.job:" + name);
            if (!(bool)(await cmd.ExecuteScalarAsync(ct))!) return false;
        }
        try { await work(ct); }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@name, 0))", conn);
            unlock.Parameters.AddWithValue("name", "auth2.job:" + name);
            await unlock.ExecuteScalarAsync(CancellationToken.None);
        }
        return true;
    }

    /// <inheritdoc />
    public async Task RunExclusiveAsync(string name, Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        string connectionString;
        await using (var scope = scopes.CreateAsyncScope())
            connectionString = scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Database.GetConnectionString()!;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@name, 0))", conn))
        {
            cmd.Parameters.AddWithValue("name", "auth2.excl:" + name);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        try { await work(ct); }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@name, 0))", conn);
            unlock.Parameters.AddWithValue("name", "auth2.excl:" + name);
            await unlock.ExecuteScalarAsync(CancellationToken.None);
        }
    }
}

/// <summary>The cleanup jobs. Each one runs under a lock, so with several instances only one does the work.</summary>
public sealed class MaintenanceJobs(IServiceScopeFactory scopes, IDistributedLock locks, IOptions<Auth2ServerOptions> options, TimeProvider clock, ILogger<MaintenanceJobs> log)
{
    /// <summary>Names of the jobs.</summary>
    public static readonly string[] Names = ["otp-cleanup", "audit-retention", "token-pruning", "key-removal"];

    /// <summary>Runs every job once. Returns the names of the jobs this instance ran (the others were held by another instance).</summary>
    public async Task<IReadOnlyList<string>> RunOnceAsync(CancellationToken ct = default)
    {
        var ran = new List<string>();
        foreach (var name in Names)
        {
            try
            {
                if (await locks.TryRunAsync(name, token => RunJobAsync(name, token), ct)) ran.Add(name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "The job {Job} failed.", name);
            }
        }
        return ran;
    }

    private async Task RunJobAsync(string name, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var now = clock.GetUtcNow();
        switch (name)
        {
            case "otp-cleanup":
            {
                using var _ = sp.GetRequiredService<IGlobalScope>().Begin();
                var db = sp.GetRequiredService<Auth2DbContext>();
                var cutoff = now.AddDays(-1);
                var n = await db.OtpCodes.Where(c => c.ExpiresAtUtc < cutoff || (c.ConsumedAtUtc != null && c.ConsumedAtUtc < cutoff)).ExecuteDeleteAsync(ct);
                if (n > 0) log.LogInformation("Removed {Count} old one-time codes.", n);
                break;
            }
            case "audit-retention":
            {
                var days = options.Value.Jobs.AuditRetentionDays;
                if (days <= 0) break;
                using var _ = sp.GetRequiredService<IGlobalScope>().Begin();
                var db = sp.GetRequiredService<Auth2DbContext>();
                var cutoff = now.AddDays(-days);
                var n = await db.AuditEvents.Where(e => e.OccurredAtUtc < cutoff).ExecuteDeleteAsync(ct);
                if (n > 0) log.LogInformation("Removed {Count} audit events older than {Days} days.", n, days);
                break;
            }
            case "token-pruning":
            {
                var threshold = now.AddDays(-14);
                await sp.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(threshold, ct);
                await sp.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(threshold, ct);
                break;
            }
            case "key-removal":
                await sp.GetRequiredService<IKeyRing>().RemoveExpiredAsync(ct);
                break;
        }
    }
}

/// <summary>Runs the cleanup jobs on a timer.</summary>
internal sealed class MaintenanceService(MaintenanceJobs jobs, IOptions<Auth2ServerOptions> options, ILogger<MaintenanceService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Jobs.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.Jobs.IntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try { await jobs.RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "The maintenance run failed."); }
        }
    }
}
