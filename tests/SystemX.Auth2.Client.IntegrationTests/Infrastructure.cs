using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SystemX.Auth2.Client;
using SystemX.Auth2.Client.DependencyInjection;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;
using Testcontainers.PostgreSql;

namespace SystemX.Auth2.Client.IntegrationTests;

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
        var name = "auth2c_" + Guid.NewGuid().ToString("N");
        await using (var conn = new NpgsqlConnection(container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name, Pooling = false }.ConnectionString;
    }
}

/// <summary>Keeps the emails and SMS that the Server sends, so a test can read the link or the code.</summary>
public sealed class CapturedMessages : IEmailSender, ISmsSender
{
    public System.Collections.Concurrent.ConcurrentQueue<EmailMessage> Emails { get; } = new();
    public System.Collections.Concurrent.ConcurrentQueue<SmsMessage> Texts { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default) { Emails.Enqueue(message); return Task.CompletedTask; }
    public Task SendAsync(SmsMessage message, CancellationToken ct = default) { Texts.Enqueue(message); return Task.CompletedTask; }

    public EmailMessage? LastEmailTo(string to) => Emails.LastOrDefault(e => e.To.Equals(to, StringComparison.OrdinalIgnoreCase));
    public SmsMessage? LastTextTo(string to) => Texts.LastOrDefault(t => t.To == to);
}

/// <summary>The real Server in memory with its own database.</summary>
public class ServerFactory(string connectionString, bool single) : WebApplicationFactory<Program>
{
    public CapturedMessages Messages { get; } = new();

    /// <summary>Where the Server's health questions go (the two-way health check): set <c>Target</c> to the handler of the client application under test.</summary>
    public ForwardingHandler Clients { get; } = new();

    /// <summary>Sends every request to <see cref="Target"/>, which can be changed at any time (a handler cannot change its inner handler after its first request).</summary>
    public sealed class ForwardingHandler : HttpMessageHandler
    {
        public HttpMessageHandler? Target { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Target is null ? throw new HttpRequestException("no client") : new HttpMessageInvoker(Target, disposeHandler: false).SendAsync(request, ct);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IEmailSender>(Messages);
            s.AddSingleton<ISmsSender>(Messages);
            s.AddHttpClient("auth2-client-health").ConfigurePrimaryHttpMessageHandler(() => Clients);
        });
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Auth2", connectionString);
        builder.UseSetting("Auth2:ProjectStatusCacheSeconds", "0");
        builder.UseSetting("Auth2:Redis:ConnectionString", "");   // appsettings.json points at the Redis of docker-compose; these tests use none
        builder.ConfigureLogging(l => l.ClearProviders());
        if (single) builder.ConfigureTestServices(s => s.ServerWithSingleProject());
    }
}

/// <summary>
/// A running Server with one application and a client built from the real Client package that talks to it.
/// Multi mode: project "Blog" with project key <c>blog</c> and its application "default". Single mode: the one application "default".
/// </summary>
public abstract class ClientTestBase : IAsyncLifetime
{
    protected virtual bool Single => false;
    protected ServerFactory Server { get; private set; } = null!;
    protected string ClientId => Single ? "default" : "blog";
    protected string ApplicationName => "default";   // the first application of a project made by CreateWithApplicationAsync is named "default"
    protected string ClientSecret { get; private set; } = "";
    protected Guid ProjectId { get; private set; }

    public async Task InitializeAsync()
    {
        Server = new ServerFactory(await SharedPostgres.CreateDatabaseAsync(), Single);
        await using var scope = Server.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().RunAsync(migrate: true, seed: true);
        var apps = scope.ServiceProvider.GetRequiredService<IProjectService>();
        if (Single)
        {
            ClientSecret = await scope.ServiceProvider.GetRequiredService<IApplicationService>().RotateSecretAsync("default");
            ProjectId = (await apps.FindByProjectKeyAsync("default"))!.Id;
        }
        else
        {
            var created = await apps.CreateWithApplicationAsync("blog", "Blog", null, null);
            ClientSecret = created.ClientSecret;
            ProjectId = created.Project.Id;
        }
    }

    public async Task DisposeAsync() => await Server.DisposeAsync();

    protected async Task UpdateSettingsAsync(Action<Server.IntegrationTests.TestEdit> change)
    {
        await using var scope = Server.Services.CreateAsyncScope();
        var apps = scope.ServiceProvider.GetRequiredService<IApplicationService>();
        var client = (await apps.ListAsync(ProjectId)).First();
        var settings = await scope.ServiceProvider.GetRequiredService<IProjectSettingsProvider>().GetAsync(ProjectId);
        var edit = new Server.IntegrationTests.TestEdit(SettingsJson.Clone(settings), client);
        change(edit);
        await scope.ServiceProvider.GetRequiredService<IProjectService>().UpdateSettingsAsync(ProjectId, edit.Project);
        await apps.UpdateAsync(client.Id, new ApplicationSpec("", RedirectUris: edit.RedirectUris, PostLogoutRedirectUris: edit.PostLogoutRedirectUris,
            AllowedScopes: edit.AllowedScopes, Settings: edit.App));
    }

    /// <summary>A client that talks to the in-memory server. <paramref name="projectKey"/> null = no <c>ServerWithMultiProject</c> call.</summary>
    protected ServiceProvider BuildClient(string? projectKey, Action<Client.Options.Auth2ClientOptions>? options = null, Action<IAuth2ClientBuilder>? configure = null, HttpMessageHandler? handler = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddAuth2Client("http://localhost", o =>
        {
            o.ApplicationName = ApplicationName;
            o.ClientId = ClientId;
            o.ClientSecret = ClientSecret;
            o.HandshakeTimeout = TimeSpan.FromSeconds(10);
            options?.Invoke(o);
        });
        if (projectKey is not null) builder.ServerWithMultiProject(projectKey);
        configure?.Invoke(builder);
        foreach (var name in new[] { Auth2ClientDefaults.HttpClientName, Auth2ClientDefaults.ResilientHttpClientName })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => handler ?? Server.Server.CreateHandler());
        return services.BuildServiceProvider();
    }
}

/// <summary>A handler that behaves like a server that is down.</summary>
internal sealed class DownHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("connection refused");
}
