using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;

namespace SystemX.Auth2.Server.Persistence.Services;

/// <summary>Creates projects and updates their settings and status.</summary>
public sealed class ProjectService(
    Auth2DbContext db,
    IGlobalScope globalScope,
    IClientProvisioner provisioner,
    IProjectSettingsProvider settingsProvider,
    IProjectStatusCache statusCache,
    Infrastructure.External.IExternalProviderRegistry externalProviders,
    IAuditService audit,
    IOptions<Auth2ServerOptions> options,
    TimeProvider clock) : IProjectService
{
    /// <inheritdoc />
    public async Task<Project> CreateAsync(string projectKey, string name, string? description, CancellationToken ct = default)
    {
        if (options.Value.Mode == ServerMode.SingleProject)
            throw new InvalidOperationException("The server has a single project. It is created by the seeder.");
        if (!ProjectKeyRules.IsValidForNewProject(projectKey))
            throw new ArgumentException("ProjectKey must use lowercase letters, digits, '-' or '_', be 3-64 characters long and must not be '_global'.", nameof(projectKey));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));

        using var _ = globalScope.Begin();
        var now = clock.GetUtcNow();
        var project = new Project { ProjectKey = projectKey, Name = name.Trim(), Description = description, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("project.created", project.Id, TargetType: "project", TargetId: project.Id.ToString()), ct);
        return project;
    }

    /// <inheritdoc />
    public async Task<ProjectCreationResult> CreateWithApplicationAsync(string projectKey, string name, string? description, string? clientId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var project = await CreateAsync(projectKey, name, description, ct);
        var created = await provisioner.CreateAsync(project, new ApplicationSpec("default", string.IsNullOrWhiteSpace(clientId) ? projectKey : clientId.Trim()), clock.GetUtcNow(), ct);
        await audit.WriteAsync(new AuditEntry("application.created", project.Id, ActorClientId: created.Application.ClientId, TargetType: "application", TargetId: created.Application.Id.ToString()), ct);
        await tx.CommitAsync(ct);
        return new ProjectCreationResult(project, created.Application, created.NewClientSecret!);
    }

    /// <inheritdoc />
    public async Task<Project?> FindAsync(Guid projectId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        return await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.Id == projectId, ct);
    }

    /// <inheritdoc />
    public async Task<Project?> FindByProjectKeyAsync(string projectKey, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        return await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectKey == projectKey, ct);
    }

    /// <inheritdoc />
    public async Task UpdateSettingsAsync(Guid projectId, ProjectSettings settings, CancellationToken ct = default)
    {
        ProjectSettingsValidator.ValidateOrThrow(settings);
        using var _ = globalScope.Begin();
        var project = await db.Projects.FirstOrDefaultAsync(a => a.Id == projectId, ct)
            ?? throw new ProjectNotFoundException("Project not found.");
        project.Settings = settings;
        project.SettingsVersion++;
        project.UpdatedAtUtc = clock.GetUtcNow();
        // The value comparer sees no change when the JSON is identical, so we force the property state.
        db.Entry(project).Property(a => a.Settings).IsModified = true;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await provisioner.ResyncProjectAsync(projectId, settings, clock.GetUtcNow(), ct);   // the consent type of every application follows the project
        await tx.CommitAsync(ct);
        settingsProvider.Invalidate(projectId);
        // the consent type of every application followed the project: every instance reads them again
        foreach (var clientId in await db.Set<Infrastructure.OpenIddict.Auth2Application>().AsNoTracking().Where(a => a.ProjectId == projectId).Select(a => a.ClientId!).ToListAsync(ct))
            statusCache.InvalidateApplication(clientId);
        await externalProviders.RefreshProjectAsync(projectId, ct);
        await audit.WriteAsync(new AuditEntry("project.settings_updated", projectId, TargetType: "project",
            TargetId: projectId.ToString(), Data: new Dictionary<string, object?> { ["settingsVersion"] = project.SettingsVersion }), ct);
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(Guid projectId, bool enabled, string? reason, Guid? actorUserId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var project = await db.Projects.FirstOrDefaultAsync(a => a.Id == projectId, ct)
            ?? throw new ProjectNotFoundException("Project not found.");
        if (project.IsGlobalProject && !enabled) throw new InvalidOperationException("The _global project cannot be disabled.");
        if (project.IsEnabled == enabled) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        project.IsEnabled = enabled;
        project.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var revoked = 0;
        if (!enabled)
        {
            // Everything that could still be used is revoked in the same transaction. The data stays.
            revoked = await provisioner.RevokeAsync(projectId, null, ct);
            await db.UserProjects.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.ActiveSessionId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveSessionId, (string?)null), ct);
        }
        await audit.WriteAsync(new AuditEntry(enabled ? "project.enabled" : "project.disabled", projectId, ActorUserId: actorUserId, TargetType: "project",
            TargetId: projectId.ToString(), Data: new Dictionary<string, object?> { ["reason"] = reason, ["revoked"] = revoked }), ct);
        await tx.CommitAsync(ct);
        statusCache.Invalidate(projectId);
    }
}
