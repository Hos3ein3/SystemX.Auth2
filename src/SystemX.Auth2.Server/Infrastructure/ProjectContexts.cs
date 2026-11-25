using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Caching;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>
/// Reads a project through <see cref="IHybridCacheService"/>: a project is not read from the database on every request. Redis (when on) holds it for all
/// instances and a request only checks its version; a change of the project raises the version (<see cref="AuthFlowCache.InvalidateProject"/>).
/// </summary>
internal static class CachedProjects
{
    public static async Task<Project> ByIdAsync(IHybridCacheService cache, Auth2DbContext db, Guid id, Func<Task> forgetReference, CancellationToken ct)
    {
        try
        {
            return await cache.GetOrCreateAsync(AuthFlowCache.Project(id), async token =>
                await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, token) ?? throw new ProjectNotFoundException("No project matches this request."),
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) }, ct);
        }
        catch (ProjectNotFoundException)
        {
            await forgetReference();   // the project is gone: a key or client id that pointed to it must not keep pointing
            throw;
        }
    }

    public static async Task<Guid> RefAsync(IHybridCacheService cache, string cacheKey, Func<CancellationToken, Task<Guid?>> find, string notFound, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(cacheKey, async token =>
            new AuthFlowCache.ProjectRef(await find(token) ?? throw new ProjectNotFoundException(notFound)),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) }, ct)).Id;
}

/// <summary>Single-project mode: always the one fixed project row (not <c>_global</c>).</summary>
public sealed class SingleProjectContext(Auth2DbContext db, IProjectScope scope, IOptions<Auth2ServerOptions> options, IHybridCacheService cache) : IProjectContext
{
    private Project? _cached;

    /// <inheritdoc />
    public async Task<Project> GetRequiredAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var name = options.Value.SingleProject.Name;
        var id = await CachedProjects.RefAsync(cache, AuthFlowCache.Single(name),
            async token => await db.Projects.AsNoTracking().Where(a => a.ProjectKey != ProjectKeyRules.GlobalProjectKey && a.Name == name).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(token),
            $"The single project '{name}' does not exist. Run the 'seed' command.", ct);
        var project = await CachedProjects.ByIdAsync(cache, db, id, () => { cache.Invalidate(AuthFlowCache.Single(name)); return Task.CompletedTask; }, ct);
        scope.Set(project.Id, null);
        return _cached = project;
    }
}

/// <summary>Multi-project mode: from the admin's selected project, else from the client id of the request (the application knows its project).</summary>
public sealed class MultiProjectContext(Auth2DbContext db, IProjectScope scope, IClientIdAccessor clientId, IProjectSelection selection, IHybridCacheService cache) : IProjectContext
{
    private Project? _cached;

    /// <inheritdoc />
    public async Task<Project> GetRequiredAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            if (!string.IsNullOrEmpty(selection.ProjectKey) && !string.Equals(_cached.ProjectKey, selection.ProjectKey, StringComparison.OrdinalIgnoreCase))
            {
                _cached = null;
            }
            else
            {
                return _cached;
            }
        }

        string referenceKey;
        Guid id;
        if (!string.IsNullOrEmpty(selection.ProjectKey))
        {
            var key = selection.ProjectKey;
            referenceKey = AuthFlowCache.ByProjectKey(key);
            id = await CachedProjects.RefAsync(cache, referenceKey,
                async token => await db.Projects.AsNoTracking().Where(a => a.ProjectKey == key).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(token),
                "No project matches this request.", ct);
        }
        else if (!string.IsNullOrEmpty(clientId.ClientId))
        {
            var client = clientId.ClientId;
            referenceKey = AuthFlowCache.ByClientId(client);
            id = await CachedProjects.RefAsync(cache, referenceKey,
                async token => await db.Set<Auth2Application>().AsNoTracking().Where(a => a.ClientId == client).Select(a => (Guid?)a.ProjectId).FirstOrDefaultAsync(token),
                "No project matches this request.", ct);
        }
        else
        {
            throw new ProjectNotFoundException("No client id or project selection is available for this request.");
        }

        var project = await CachedProjects.ByIdAsync(cache, db, id, () => { cache.Invalidate(referenceKey); return Task.CompletedTask; }, ct);
        scope.Set(project.Id, project.ProjectKey);
        return _cached = project;
    }
}

/// <summary>Chooses the strategy at runtime from <see cref="Auth2ServerOptions.Mode"/>. Used until <c>ServerWithSingleProject()</c> replaces it.</summary>
internal sealed class ModeProjectContext(IOptions<Auth2ServerOptions> options, IServiceProvider sp) : IProjectContext
{
    private IProjectContext? _inner;

    public Task<Project> GetRequiredAsync(CancellationToken ct = default)
    {
        _inner ??= options.Value.Mode == SystemX.Auth2.Contracts.Enums.ServerMode.SingleProject
            ? ActivatorUtilities.CreateInstance<SingleProjectContext>(sp)
            : ActivatorUtilities.CreateInstance<MultiProjectContext>(sp);
        return _inner.GetRequiredAsync(ct);
    }
}

/// <summary>The calling application, read from the client id of the request through <see cref="IHybridCacheService"/> (Redis checked by version). Scoped.</summary>
public sealed class ClientApplicationContext(Auth2DbContext db, IClientIdAccessor clientId, IHybridCacheService cache) : IClientApplicationContext
{
    private ClientApplicationInfo? _cached;
    private string? _cachedFor;

    /// <inheritdoc />
    public async Task<ClientApplicationInfo?> GetAsync(CancellationToken ct = default)
    {
        var id = clientId.ClientId;
        if (string.IsNullOrEmpty(id)) return null;
        if (_cached is not null && _cachedFor == id) return _cached;
        // An unknown client id is not cached (the factory throws), so a new application is seen at once.
        ClientApplicationInfo info;
        try
        {
            info = await cache.GetOrCreateAsync(AuthFlowCache.Client(id), async token =>
            {
                var entity = await db.Set<Auth2Application>().AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == id, token);
                return entity is null ? throw new ApplicationNotCachedException() : OpenIddictClientProvisioner.ToInfo(entity);
            }, new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) }, ct);
        }
        catch (ApplicationNotCachedException) { return null; }
        _cachedFor = id;
        return _cached = info;
    }

    /// <inheritdoc />
    public async Task<ClientApplicationInfo> GetRequiredAsync(CancellationToken ct = default) =>
        await GetAsync(ct) ?? throw new ProjectNotFoundException("No application matches this request.");

    private sealed class ApplicationNotCachedException : Exception;
}
