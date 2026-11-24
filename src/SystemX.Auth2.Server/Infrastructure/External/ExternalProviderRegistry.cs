using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Operations;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>The list of enabled external providers of all applications. Always answers from memory.</summary>
public interface IExternalProviderRegistry
{
    /// <summary>One enabled provider, or null when unknown or disabled.</summary>
    RegisteredProvider? Get(Guid applicationId, string name);
    /// <summary>The enabled providers of an application.</summary>
    IReadOnlyList<RegisteredProvider> ListForProject(Guid applicationId);
    /// <summary>The provider that handles an email domain, or null.</summary>
    RegisteredProvider? FindByEmailDomain(Guid applicationId, string domain);
    /// <summary>Finds a provider by the project slug of a URL.</summary>
    RegisteredProvider? FindByUrl(string projectSlug, string name);
    /// <summary>Loads one application again now (used after a change on this instance or a Redis message).</summary>
    Task RefreshProjectAsync(Guid applicationId, CancellationToken ct = default);
    /// <summary>Looks for applications whose settings version changed and loads them. Returns how many were reloaded.</summary>
    Task<int> RefreshChangedAsync(CancellationToken ct = default);
    /// <summary>Raised after the list changed.</summary>
    event Action? Changed;
}

/// <inheritdoc />
public sealed class ExternalProviderRegistry(IServiceScopeFactory scopes, IOptions<Auth2ServerOptions> options, ILogger<ExternalProviderRegistry> log) : IExternalProviderRegistry
{
    private readonly object _gate = new();
    // application id -> (settings version, providers)
    private FrozenDictionary<Guid, (int Version, string Slug, bool Enabled, IReadOnlyList<RegisteredProvider> Providers)> _apps =
        FrozenDictionary<Guid, (int, string, bool, IReadOnlyList<RegisteredProvider>)>.Empty;
    private FrozenDictionary<string, RegisteredProvider> _byUrl = FrozenDictionary<string, RegisteredProvider>.Empty;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public RegisteredProvider? Get(Guid applicationId, string name) =>
        _apps.TryGetValue(applicationId, out var a) ? a.Providers.FirstOrDefault(p => p.Name == name) : null;

    /// <inheritdoc />
    public IReadOnlyList<RegisteredProvider> ListForProject(Guid applicationId) =>
        _apps.TryGetValue(applicationId, out var a) ? a.Providers : [];

    /// <inheritdoc />
    public RegisteredProvider? FindByEmailDomain(Guid applicationId, string domain) =>
        ListForProject(applicationId).FirstOrDefault(p => p.Settings.EmailDomains.Contains(domain, StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc />
    public RegisteredProvider? FindByUrl(string projectSlug, string name) => _byUrl.GetValueOrDefault(projectSlug + "/" + name);

    /// <inheritdoc />
    public async Task RefreshProjectAsync(Guid applicationId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var app = await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        Apply(applicationId, app is null ? null : Build(app));
    }

    /// <inheritdoc />
    public async Task<int> RefreshChangedAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var versions = await db.Projects.AsNoTracking().Select(a => new { a.Id, a.SettingsVersion, a.IsEnabled }).ToListAsync(ct);
        var current = _apps;
        var changed = 0;
        foreach (var v in versions)
        {
            if (current.TryGetValue(v.Id, out var known) && known.Version == v.SettingsVersion && known.Enabled == v.IsEnabled) continue;
            var app = await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.Id == v.Id, ct);
            Apply(v.Id, app is null ? null : Build(app));
            changed++;
        }
        foreach (var gone in current.Keys.Where(id => versions.All(v => v.Id != id)).ToList())
        {
            Apply(gone, null);
            changed++;
        }
        return changed;
    }

    private (int Version, string Slug, bool Enabled, IReadOnlyList<RegisteredProvider> Providers) Build(Domain.Entities.Project app)
    {
        var slug = options.Value.Mode == ServerMode.SingleProject ? ExternalProviderUrls.SingleModeSlug : app.ProjectKey;
        IReadOnlyList<RegisteredProvider> list = app.IsEnabled
            ? [.. app.Settings.ExternalProviders.Where(p => p.Enabled).Select(p => new RegisteredProvider(app.Id, slug, p, app.SettingsVersion))]
            : [];
        return (app.SettingsVersion, slug, app.IsEnabled, list);
    }

    private void Apply(Guid applicationId, (int Version, string Slug, bool Enabled, IReadOnlyList<RegisteredProvider> Providers)? entry)
    {
        lock (_gate)
        {
            var map = _apps.ToDictionary();
            if (entry is null) map.Remove(applicationId); else map[applicationId] = entry.Value;
            _apps = map.ToFrozenDictionary();
            _byUrl = map.Values.SelectMany(v => v.Providers).ToDictionary(p => p.ProjectSlug + "/" + p.Name).ToFrozenDictionary();
        }
        log.LogInformation("External providers of application {ProjectId} loaded: {Count}.", applicationId, entry?.Providers.Count ?? 0);
        Changed?.Invoke();
    }
}

/// <summary>Loads providers at start, polls for changes, and listens to Redis (when on) so that all instances see a change at once.</summary>
internal sealed class ExternalProviderRefreshService(
    IExternalProviderRegistry registry, IOptions<Auth2ServerOptions> options, IServiceProvider services,
    TimeProvider clock, ILogger<ExternalProviderRefreshService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TryAsync(() => registry.RefreshChangedAsync(stoppingToken));
        var redis = services.GetService<RedisConnection>();
        if (redis is not null)
        {
            redis.Multiplexer.GetSubscriber().Subscribe(StackExchange.Redis.RedisChannel.Literal(redis.Key("settings-changed")), (channel, value) =>
            {
                if (Guid.TryParseExact((string?)value, "N", out var id)) _ = TryAsync(() => registry.RefreshProjectAsync(id, stoppingToken));
            });
        }
        var every = TimeSpan.FromSeconds(Math.Max(1, options.Value.ExternalProviders.PollSeconds));
        using var timer = new PeriodicTimer(every, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await TryAsync(() => registry.RefreshChangedAsync(stoppingToken));
    }

    private async Task TryAsync(Func<Task> work)
    {
        try { await work(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogWarning(ex, "Could not refresh the external providers. The old list stays in use."); }
    }
}
