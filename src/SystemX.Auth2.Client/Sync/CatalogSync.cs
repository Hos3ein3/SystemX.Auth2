using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Sync;

/// <summary>What the client last read from the server (policies <c>Pull</c> and <c>Both</c>). Handy for screens that list permissions, roles and groups.</summary>
public interface IAuth2Catalog
{
    /// <summary>Permissions of the application, or empty before the first read.</summary>
    IReadOnlyList<PermissionDto> Permissions { get; }
    /// <summary>Roles with their permission keys.</summary>
    IReadOnlyList<RoleDto> Roles { get; }
    /// <summary>Groups with their roles and permission keys.</summary>
    IReadOnlyList<GroupDto> Groups { get; }
    /// <summary>Custom scopes this application allows.</summary>
    IReadOnlyList<ScopeDto> Scopes { get; }
    /// <summary>When the last successful read finished, or null.</summary>
    DateTimeOffset? LastReadUtc { get; }
}

internal sealed class Auth2Catalog : IAuth2Catalog
{
    private volatile IReadOnlyList<PermissionDto> _permissions = [];
    private volatile IReadOnlyList<RoleDto> _roles = [];
    private volatile IReadOnlyList<GroupDto> _groups = [];
    private volatile IReadOnlyList<ScopeDto> _scopes = [];
    private long _last;

    public IReadOnlyList<PermissionDto> Permissions => _permissions;
    public IReadOnlyList<RoleDto> Roles => _roles;
    public IReadOnlyList<GroupDto> Groups => _groups;
    public IReadOnlyList<ScopeDto> Scopes => _scopes;
    public DateTimeOffset? LastReadUtc => Interlocked.Read(ref _last) is var t and > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(t) : null;

    public void Set(IReadOnlyList<PermissionDto>? permissions, IReadOnlyList<RoleDto>? roles, IReadOnlyList<GroupDto>? groups, IReadOnlyList<ScopeDto>? scopes, DateTimeOffset now)
    {
        if (permissions is not null) _permissions = permissions;
        if (roles is not null) _roles = roles;
        if (groups is not null) _groups = groups;
        if (scopes is not null) _scopes = scopes;
        Interlocked.Exchange(ref _last, now.ToUnixTimeMilliseconds());
    }
}

/// <summary>
/// Syncs one kind of catalog (permissions or roles) with the server. It sends the list only when it changed since the last success
/// (a hash of the sorted list), and tries again with a growing wait when the server fails.
/// </summary>
public sealed class CatalogSyncService<TItem>(string name, Func<IReadOnlyList<TItem>> collect,
    Func<IReadOnlyList<TItem>, CancellationToken, Task<Result<SyncResponse>>> push, IOptions<Auth2ClientOptions> options, TimeProvider clock, ILogger log)
{
    private string? _lastHash;

    /// <summary>Sends the list when it changed. Returns true when the server has the current list (sent now, or unchanged since the last success).</summary>
    public async Task<bool> PushAsync(CancellationToken ct)
    {
        var items = collect();
        var hash = Hash(items);
        if (hash == _lastHash) { log.LogDebug("The {Catalog} did not change since the last sync.", name); return true; }
        if (items.Count == 0) { _lastHash = hash; return true; }

        var o = options.Value;
        var delay = o.HandshakeInitialDelay;
        for (var attempt = 0; ; attempt++)
        {
            var result = await push(items, ct);
            if (result.IsSuccess)
            {
                _lastHash = hash;
                log.LogInformation("Synced {Count} {Catalog}: {Added} added, {Updated} updated.", items.Count, name, result.Value!.Added, result.Value.Updated);
                return true;
            }
            var code = result.Error!.Code;
            // Wrong list or not allowed: trying again changes nothing.
            var transient = code is Auth2ClientErrorCodes.ServerUnavailable or Contracts.Constants.Auth2ErrorCodes.RateLimited;
            if (!transient || attempt >= o.SyncMaxRetries)
            {
                log.LogError("Syncing the {Catalog} failed ({Code}).", name, code);
                return false;
            }
            log.LogWarning("Syncing the {Catalog} failed ({Code}). Trying again in {Delay}.", name, code, delay);
            await Task.Delay(delay, clock, ct);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, o.HandshakeMaxDelay.Ticks));
        }
    }

    private static string Hash(IReadOnlyList<TItem> items) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(items)));
}

/// <summary>
/// Runs the sync <b>after the application started</b> (so every endpoint is mapped and can be scanned) and after a successful handshake.
/// Push: sends scopes, permissions, roles, then groups. Pull: reads scopes, permissions, roles and groups now and every <c>CatalogRefreshInterval</c>.
/// A failure is logged and the application carries on, unless <c>FailOnSyncError</c> is true.
/// </summary>
public sealed class Auth2SyncHostedService(IHostApplicationLifetime lifetime, Auth2ClientState state,
    IPermissionScanner permissionScanner, IRoleScanner roleScanner, IScopeScanner scopeScanner, IGroupScanner groupScanner,
    IAuth2ApiClientService api, IOptions<Auth2ClientOptions> client, IOptions<Auth2PermissionOptions> permissions, IOptions<Auth2RoleOptions> roles,
    IOptions<Auth2GroupOptions> groups, IOptions<Auth2ScopeOptions> scopes, IAuth2Catalog catalog, TimeProvider clock, ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger _log = loggers.CreateLogger<Auth2SyncHostedService>();

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var p = permissions.Value.SyncPolicy;
        var r = roles.Value.SyncPolicy;
        var g = groups.Value.SyncPolicy;
        var s = scopes.Value.SyncPolicy;
        if (p == PermissionSyncPolicy.None && r == RoleSyncPolicy.None && g == GroupSyncPolicy.None && s == ScopeSyncPolicy.None) return;

        var pushScopes = s is ScopeSyncPolicy.Push or ScopeSyncPolicy.Both;
        var pushPermissions = p is PermissionSyncPolicy.Push or PermissionSyncPolicy.Both;
        var pushRoles = r is RoleSyncPolicy.Push or RoleSyncPolicy.Both;
        var pushGroups = g is GroupSyncPolicy.Push or GroupSyncPolicy.Both;
        var pullScopes = s is ScopeSyncPolicy.Pull or ScopeSyncPolicy.Both;
        var pullPermissions = p is PermissionSyncPolicy.Pull or PermissionSyncPolicy.Both;
        var pullRoles = r is RoleSyncPolicy.Pull or RoleSyncPolicy.Both;
        var pullGroups = g is GroupSyncPolicy.Pull or GroupSyncPolicy.Both;
        var pulls = pullScopes || pullPermissions || pullRoles || pullGroups;

        try
        {
            await WaitForStartedAsync(stoppingToken);
            await state.WaitUntilReadyAsync(stoppingToken);   // no sync before a good handshake

            var scopeSync = new CatalogSyncService<ScopeDefinition>("scopes", () => scopeScanner.Scan(),
                async (items, ct) => await api.SyncScopesAsync(new ScopeSyncRequest(items), ct), client, clock, _log);
            var permissionSync = new CatalogSyncService<PermissionDefinition>("permissions", () => permissionScanner.Scan(),
                async (items, ct) => await api.SyncPermissionsAsync(new PermissionSyncRequest(items), ct), client, clock, _log);
            var roleSync = new CatalogSyncService<RoleDefinition>("roles", () => roleScanner.Scan(),
                async (items, ct) => await api.SyncRolesAsync(new RoleSyncRequest(items), ct), client, clock, _log);
            var groupSync = new CatalogSyncService<GroupDefinition>("groups", () => groupScanner.Scan(),
                async (items, ct) => await api.SyncGroupsAsync(new GroupSyncRequest(null, items), ct), client, clock, _log);

            while (!stoppingToken.IsCancellationRequested)
            {
                var ok = true;
                // Order matters: scopes and permissions first, roles can refer to permissions, groups last.
                if (pushScopes) ok &= await scopeSync.PushAsync(stoppingToken);
                if (pushPermissions) ok &= await permissionSync.PushAsync(stoppingToken);
                if (pushRoles) ok &= await roleSync.PushAsync(stoppingToken);
                if (pushGroups) ok &= await groupSync.PushAsync(stoppingToken);
                if (pulls) ok &= await PullAsync(pullScopes, pullPermissions, pullRoles, pullGroups, stoppingToken);
                if (!ok && client.Value.FailOnSyncError)
                    throw new Auth2StartupException("Syncing with the Auth2 server failed (FailOnSyncError is on).", HandshakeStatus.ServerUnavailable);

                // Push once per start (an unchanged list is skipped anyway); keep reading the catalogs when asked to.
                if (!pulls) return;
                await Task.Delay(client.Value.CatalogRefreshInterval, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
    }

    private async Task<bool> PullAsync(bool scopes, bool permissions, bool roles, bool groups, CancellationToken ct)
    {
        IReadOnlyList<ScopeDto>? scopesRead = null; IReadOnlyList<PermissionDto>? perms = null; IReadOnlyList<RoleDto>? rolesRead = null; IReadOnlyList<GroupDto>? groupsRead = null;
        if (scopes)
        {
            var result = await api.GetScopesAsync(ct);
            if (!result.IsSuccess) return Failed("scopes", result.Error!.Code);
            scopesRead = result.Value;
        }
        if (permissions)
        {
            var result = await api.GetPermissionsAsync(ct);
            if (!result.IsSuccess) return Failed("permissions", result.Error!.Code);
            perms = result.Value;
        }
        if (roles)
        {
            var result = await api.GetRolesAsync(ct);
            if (!result.IsSuccess) return Failed("roles", result.Error!.Code);
            rolesRead = result.Value;
        }
        if (groups)
        {
            var result = await api.GetGroupsAsync(ct);
            if (!result.IsSuccess) return Failed("groups", result.Error!.Code);
            groupsRead = result.Value;
        }
        ((Auth2Catalog)catalog).Set(perms, rolesRead, groupsRead, scopesRead, clock.GetUtcNow());
        return true;
    }

    private bool Failed(string what, string code)
    {
        _log.LogError("Reading the {Catalog} from the Auth2 server failed ({Code}). The last known list stays.", what, code);
        return false;
    }

    private Task WaitForStartedAsync(CancellationToken ct)
    {
        if (lifetime.ApplicationStarted.IsCancellationRequested) return Task.CompletedTask;
        var started = new TaskCompletionSource();
        lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        return started.Task.WaitAsync(ct);
    }
}

/// <summary>
/// Checks every catalog when the host starts: the providers and factories are called with the service provider, then all sources are merged.
/// A wrong key, a provider that throws or two sources that disagree stop the start with a message that names the source.
/// It runs before the first sync (the sync waits for <c>ApplicationStarted</c>).
/// </summary>
internal sealed class Auth2CatalogStartupService(IServiceProvider services, IOptions<Auth2PermissionOptions> permissions, IOptions<Auth2RoleOptions> roles,
    IOptions<Auth2GroupOptions> groups, IOptions<Auth2ScopeOptions> scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (permissions.Value.SyncPolicy != PermissionSyncPolicy.None) await permissions.Value.ResolveAsync(services, ct);
        if (roles.Value.SyncPolicy != RoleSyncPolicy.None) await roles.Value.ResolveAsync(services, ct);
        if (groups.Value.SyncPolicy != GroupSyncPolicy.None) await groups.Value.ResolveAsync(services, ct);
        if (scopes.Value.SyncPolicy != ScopeSyncPolicy.None) await scopes.Value.ResolveAsync(services, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
