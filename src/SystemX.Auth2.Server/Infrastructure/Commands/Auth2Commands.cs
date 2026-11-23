using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Server.Persistence.Seeding;

namespace SystemX.Auth2.Server.Infrastructure.Commands;

/// <summary>Command line commands of the Server executable: <c>migrate</c>, <c>seed</c>, <c>create-application</c>.</summary>
public static class Auth2Commands
{
    private static readonly string[] Known = ["migrate", "seed", "create-application"];

    /// <summary>True when the first argument is a command.</summary>
    public static bool IsCommand(string[] args) => args.Length > 0 && Known.Contains(args[0], StringComparer.Ordinal);

    /// <summary>Runs a command. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(string[] args, IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Auth2.Commands");
        try
        {
            switch (args[0])
            {
                case "migrate":
                    await sp.GetRequiredService<DatabaseInitializer>().RunAsync(migrate: true, seed: true, ct);
                    log.LogInformation("Migrations applied and seed done.");
                    return 0;
                case "seed":
                    await sp.GetRequiredService<DatabaseInitializer>().RunAsync(migrate: false, seed: true, ct);
                    log.LogInformation("Seed done.");
                    return 0;
                case "create-application":
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("Usage: create-application <projectKey> <name>");
                        return 2;
                    }
                    var app = await sp.GetRequiredService<IApplicationService>().CreateAsync(args[1], args[2], null, null, ct);
                    log.LogInformation("Application {ProjectKey} created with id {Id}.", app.ProjectKey, app.Id);
                    return 0;
                default:
                    return 2;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            log.LogError("Command failed: {Message}", ex.Message);
            return 1;
        }
    }
}
