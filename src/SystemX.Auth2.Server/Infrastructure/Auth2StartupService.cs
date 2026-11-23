using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Server.Persistence.Seeding;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>
/// Startup work. Development: migrate and seed. Other environments: migrate only when <c>Auth2:AutoMigrate</c> is true,
/// seed only when <c>Auth2:AutoSeed</c> is true. In Production it warns when the admin still has the default password.
/// </summary>
public sealed class Auth2StartupService(IServiceProvider services, IOptions<Auth2ServerOptions> options, IHostEnvironment env, ILogger<Auth2StartupService> log) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct)
    {
        var dev = env.IsDevelopment();
        var migrate = dev || options.Value.AutoMigrate;
        var seed = dev || options.Value.AutoSeed;

        await using var scope = services.CreateAsyncScope();
        if (migrate || seed)
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().RunAsync(migrate, seed, ct);

        if (env.IsProduction())
        {
            try { await scope.ServiceProvider.GetRequiredService<Auth2Seeder>().WarnIfDefaultPasswordAsync(ct); }
            catch (Exception ex) { log.LogWarning(ex, "Could not check the default admin password."); }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
