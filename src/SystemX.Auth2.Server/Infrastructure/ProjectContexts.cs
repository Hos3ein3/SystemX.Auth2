using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>Single-project mode: always the one fixed project row (not <c>_global</c>).</summary>
public sealed class SingleProjectContext(Auth2DbContext db, IProjectScope scope, IOptions<Auth2ServerOptions> options) : IProjectContext
{
    private Project? _cached;

    /// <inheritdoc />
    public async Task<Project> GetRequiredAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var name = options.Value.SingleProject.Name;
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectKey != ProjectKeyRules.GlobalProjectKey && a.Name == name, ct)
            ?? throw new ProjectNotFoundException($"The single project '{name}' does not exist. Run the 'seed' command.");
        scope.Set(project.Id, null);
        return _cached = project;
    }
}

/// <summary>Multi-project mode: from the admin's selected project, else from the client id of the request (the application knows its project).</summary>
public sealed class MultiProjectContext(Auth2DbContext db, IProjectScope scope, IClientIdAccessor clientId, IProjectSelection selection) : IProjectContext
{
    private Project? _cached;

    /// <inheritdoc />
    public async Task<Project> GetRequiredAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        Project? project;
        if (!string.IsNullOrEmpty(selection.ProjectKey))
        {
            var key = selection.ProjectKey;
            project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectKey == key, ct);
        }
        else if (!string.IsNullOrEmpty(clientId.ClientId))
        {
            var id = clientId.ClientId;
            project = await (from a in db.Set<Auth2Application>().AsNoTracking()
                             join p in db.Projects.AsNoTracking() on a.ProjectId equals p.Id
                             where a.ClientId == id
                             select p).FirstOrDefaultAsync(ct);
        }
        else
        {
            throw new ProjectNotFoundException("No client id or project selection is available for this request.");
        }

        if (project is null) throw new ProjectNotFoundException("No project matches this request.");
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

/// <summary>The calling application, read from the client id of the request. Scoped.</summary>
public sealed class ClientApplicationContext(Auth2DbContext db, IClientIdAccessor clientId) : IClientApplicationContext
{
    private ClientApplicationInfo? _cached;
    private string? _cachedFor;

    /// <inheritdoc />
    public async Task<ClientApplicationInfo?> GetAsync(CancellationToken ct = default)
    {
        var id = clientId.ClientId;
        if (string.IsNullOrEmpty(id)) return null;
        if (_cached is not null && _cachedFor == id) return _cached;
        var entity = await db.Set<Auth2Application>().AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == id, ct);
        if (entity is null) return null;
        _cachedFor = id;
        return _cached = OpenIddictClientProvisioner.ToInfo(entity);
    }

    /// <inheritdoc />
    public async Task<ClientApplicationInfo> GetRequiredAsync(CancellationToken ct = default) =>
        await GetAsync(ct) ?? throw new ProjectNotFoundException("No application matches this request.");
}
