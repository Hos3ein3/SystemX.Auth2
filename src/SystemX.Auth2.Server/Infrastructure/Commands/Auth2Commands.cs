using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Server.Persistence.Seeding;

namespace SystemX.Auth2.Server.Infrastructure.Commands;

/// <summary>Command line commands of the Server executable: <c>migrate</c>, <c>seed</c>, <c>create-project</c>, <c>create-application</c>, <c>rotate-client-secret</c>, <c>rotate-keys</c>, <c>create-admin</c>.</summary>
public static class Auth2Commands
{
    private static readonly string[] Known = ["migrate", "seed", "create-project", "create-application", "rotate-client-secret", "rotate-keys", "create-admin"];

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
                case "create-project":
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("Usage: create-project <projectKey> <name>");
                        return 2;
                    }
                    var project = await sp.GetRequiredService<IProjectService>().CreateAsync(args[1], args[2], null, ct);
                    log.LogInformation("Project {ProjectKey} created with id {Id}. Add an application with: create-application {ProjectKey} <name>.", project.ProjectKey, project.Id, project.ProjectKey);
                    return 0;
                case "create-application":
                {
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("Usage: create-application <projectKey> <name> [clientId]");
                        return 2;
                    }
                    var owner = await sp.GetRequiredService<IProjectService>().FindByProjectKeyAsync(args[1], ct);
                    if (owner is null)
                    {
                        Console.Error.WriteLine($"No project with key '{args[1]}'. Create it first: create-project {args[1]} <name>");
                        return 1;
                    }
                    var createdApp = await sp.GetRequiredService<IApplicationService>().CreateAsync(owner.Id, new ApplicationSpec(args[2], args.Length > 3 ? args[3] : null), ct);
                    log.LogInformation("Application {Name} created in project {ProjectKey} with id {Id}. Client id: {ClientId}.", createdApp.Application.Name, owner.ProjectKey, createdApp.Application.Id, createdApp.Application.ClientId);
                    // The secret is written to the console only, never to the log. It cannot be shown again.
                    Console.WriteLine($"Client secret (shown once): {createdApp.ClientSecret}");
                    return 0;
                }
                case "rotate-client-secret":
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("Usage: rotate-client-secret <clientId>");
                        return 2;
                    }
                    Console.WriteLine($"New client secret (shown once): {await sp.GetRequiredService<IApplicationService>().RotateSecretAsync(args[1], ct)}");
                    return 0;
                case "rotate-keys":
                {
                    var keys = await sp.GetRequiredService<IKeyRing>().RotateAsync(ct);
                    await sp.GetRequiredService<IAuditService>().WriteAsync(new AuditEntry("keys.rotated", null, TargetType: "keys"), ct);
                    log.LogInformation("Keys rotated. {Count} keys exist now; the old keys keep checking old tokens until they are removed.", keys.Count);
                    foreach (var k in keys) Console.WriteLine($"{k.Purpose,-10} {k.Thumbprint}  until {k.NotAfterUtc:u}  {(k.RetiredAtUtc is null ? "current" : "retired, removed after " + k.RemoveAtUtc?.ToString("u"))}");
                    return 0;
                }
                case "create-admin":
                {
                    // create-admin <userName> <email>   (the password is read from the AUTH2_ADMIN_PASSWORD variable, never from the command line)
                    var password = Environment.GetEnvironmentVariable("AUTH2_ADMIN_PASSWORD");
                    if (args.Length < 3 || string.IsNullOrEmpty(password))
                    {
                        Console.Error.WriteLine("Usage: create-admin <userName> <email>   (set the password in the AUTH2_ADMIN_PASSWORD environment variable)");
                        return 2;
                    }
                    await sp.GetRequiredService<Auth2Seeder>().CreateAdminAsync(args[1], args[2], password, ct);
                    log.LogInformation("GlobalAdmin {UserName} created.", args[1]);
                    return 0;
                }
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
