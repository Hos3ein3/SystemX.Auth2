using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SystemX.Auth2.Server.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>One PostgreSQL 15 container for the whole test run. Each test gets its own database inside it.</summary>
internal static class SharedPostgres
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(async () =>
    {
        var c = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await c.StartAsync();
        return c;
    });

    public static async Task<string> CreateDatabaseAsync()
    {
        var container = await Container.Value;
        var name = "auth2_" + Guid.NewGuid().ToString("N");
        await using (var conn = new NpgsqlConnection(container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name, Pooling = false }.ConnectionString;
    }
}

/// <summary>One Redis container for the whole test run. Tests use their own key prefix, so they do not meet each other.</summary>
internal static class SharedRedis
{
    private static readonly Lazy<Task<RedisContainer>> Container = new(async () =>
    {
        var c = new RedisBuilder("redis:7-alpine").Build();
        await c.StartAsync();
        return c;
    });

    public static async Task<string> ConnectionStringAsync() => (await Container.Value).GetConnectionString();
}

/// <summary>Collects log messages so tests can check that no secret was logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);
    public void Dispose() { }

    private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Entries.Enqueue((logLevel, formatter(state, exception) + (exception?.ToString() ?? "")));
    }
}

/// <summary>Replaces the email and SMS senders. Tests read the links and codes from <see cref="Emails"/> and <see cref="Texts"/>.</summary>
public sealed class CapturingSender : SystemX.Auth2.Server.Application.IEmailSender, SystemX.Auth2.Server.Application.ISmsSender
{
    public ConcurrentQueue<SystemX.Auth2.Server.Application.EmailMessage> Emails { get; } = new();
    public ConcurrentQueue<SystemX.Auth2.Server.Application.SmsMessage> Texts { get; } = new();

    public Task SendAsync(SystemX.Auth2.Server.Application.EmailMessage message, CancellationToken ct = default) { Emails.Enqueue(message); return Task.CompletedTask; }
    public Task SendAsync(SystemX.Auth2.Server.Application.SmsMessage message, CancellationToken ct = default) { Texts.Enqueue(message); return Task.CompletedTask; }

    /// <summary>The newest message sent to this address or number, with its data (token, link, code).</summary>
    public IReadOnlyDictionary<string, string>? LastEmailTo(string to) => Emails.LastOrDefault(e => e.To.Equals(to, StringComparison.OrdinalIgnoreCase))?.Data;
    public IReadOnlyDictionary<string, string>? LastTextTo(string to) => Texts.LastOrDefault(e => e.To == to)?.Data;
    public int CountTo(string to) => Emails.Count(e => e.To.Equals(to, StringComparison.OrdinalIgnoreCase)) + Texts.Count(t => t.To == to);
}

/// <summary>The Server in memory with its own fresh database. Use <see cref="InitializeAsync"/> before first use.</summary>
public class Auth2ServerFactory : WebApplicationFactory<Program>
{
    private string _connectionString = "";

    public CapturingLoggerProvider Logs { get; } = new();

    public string ConnectionString => _connectionString;

    public CapturingSender Messages { get; } = new();

    protected virtual void ConfigureAuth2(IServiceCollection services)
    {
        services.AddSingleton<SystemX.Auth2.Server.Application.IEmailSender>(Messages);
        services.AddSingleton<SystemX.Auth2.Server.Application.ISmsSender>(Messages);
        Customize?.Invoke(services);
    }

    /// <summary>Extra service changes for one test class. Set before the first use of <c>Services</c>.</summary>
    public Action<IServiceCollection>? Customize { get; set; }

    /// <summary>Extra configuration values (for example the Redis connection string). Set before the first use of <c>Services</c>.</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    public async Task InitializeAsync() => _connectionString = await SharedPostgres.CreateDatabaseAsync();

    /// <summary>Makes this instance use the database of another one, like a second server behind the same load balancer.</summary>
    public void ShareDatabaseWith(Auth2ServerFactory other)
    {
        _connectionString = other.ConnectionString;
        foreach (var (key, value) in other.Settings) Settings[key] = value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Auth2", _connectionString);
        builder.UseSetting("Auth2:ProjectStatusCacheSeconds", "0");   // tests change the database directly and expect the change at once
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(ConfigureAuth2);
    }
}

/// <summary>Server with <c>ServerWithSingleProject()</c>.</summary>
public sealed class SingleModeFactory : Auth2ServerFactory
{
    protected override void ConfigureAuth2(IServiceCollection services)
    {
        base.ConfigureAuth2(services);
        services.ServerWithSingleProject();
    }
}

/// <summary>Base class: creates a factory and a database per test.</summary>
public abstract class ServerTestBase : IAsyncLifetime
{
    protected virtual bool SingleMode => false;
    /// <summary>True to run the server with Redis (a container shared by the test run, with a private key prefix).</summary>
    protected virtual bool UseRedis => false;
    protected Auth2ServerFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Factory = SingleMode ? new SingleModeFactory() : new Auth2ServerFactory();
        Factory.Customize = Customize;
        if (UseRedis)
        {
            Factory.Settings["Auth2:Redis:ConnectionString"] = await SharedRedis.ConnectionStringAsync();
            Factory.Settings["Auth2:Redis:KeyPrefix"] = "t" + Guid.NewGuid().ToString("N");
        }
        await Factory.InitializeAsync();
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();

    /// <summary>Override to change services for the whole test class.</summary>
    protected virtual void Customize(IServiceCollection services) { }

    /// <summary>A scope that works for the application with this client id (like a request with a client token).</summary>
    protected AsyncServiceScope ScopeFor(string clientId)
    {
        var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<SystemX.Auth2.Server.Application.IClientIdAccessor>().ClientId = clientId;
        return scope;
    }

    protected AsyncServiceScope NewScope() => Factory.Services.CreateAsyncScope();

    /// <summary>Applies migrations and seeds, like the <c>migrate</c> command.</summary>
    protected async Task MigrateAndSeedAsync()
    {
        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().RunAsync(migrate: true, seed: true);
    }

    protected async Task SeedAgainAsync()
    {
        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().RunAsync(migrate: false, seed: true);
    }
}
