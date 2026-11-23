using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SystemX.Auth2.Server.Persistence;
using Testcontainers.PostgreSql;

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

/// <summary>The Server in memory with its own fresh database. Use <see cref="InitializeAsync"/> before first use.</summary>
public class Auth2ServerFactory : WebApplicationFactory<Program>
{
    private string _connectionString = "";

    public CapturingLoggerProvider Logs { get; } = new();

    public string ConnectionString => _connectionString;

    protected virtual void ConfigureAuth2(IServiceCollection services) { }

    public async Task InitializeAsync() => _connectionString = await SharedPostgres.CreateDatabaseAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Auth2", _connectionString);
        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(ConfigureAuth2);
    }
}

/// <summary>Server with <c>ServerWithSingleApplication()</c>.</summary>
public sealed class SingleModeFactory : Auth2ServerFactory
{
    protected override void ConfigureAuth2(IServiceCollection services) => services.ServerWithSingleApplication();
}

/// <summary>Base class: creates a factory and a database per test.</summary>
public abstract class ServerTestBase : IAsyncLifetime
{
    protected virtual bool SingleMode => false;
    protected Auth2ServerFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Factory = SingleMode ? new SingleModeFactory() : new Auth2ServerFactory();
        await Factory.InitializeAsync();
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();

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
