using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;

namespace SystemX.Auth2.Server.Persistence.Services;

/// <summary>Creates and changes applications (clients of a project). All writes go through <see cref="IClientProvisioner"/>.</summary>
public sealed class ApplicationService(
    Auth2DbContext db,
    IGlobalScope globalScope,
    IClientProvisioner provisioner,
    IProjectStatusCache statusCache,
    Infrastructure.External.IExternalProviderRegistry externalProviders,
    IAuditService audit,
    TimeProvider clock) : IApplicationService
{
    /// <inheritdoc />
    public async Task<ApplicationCreationResult> CreateAsync(Guid projectId, ApplicationSpec spec, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ProjectNotFoundException("Project not found.");
        Validate(spec, requireName: true);
        if (await db.Set<Auth2Application>().AnyAsync(a => a.ProjectId == projectId && a.Name == spec.Name.Trim(), ct))
            throw new InvalidOperationException($"The project already has an application named '{spec.Name.Trim()}'.");

        var created = await provisioner.CreateAsync(project, spec, clock.GetUtcNow(), ct);
        await audit.WriteAsync(new AuditEntry("application.created", projectId, ActorClientId: created.Application.ClientId,
            TargetType: "application", TargetId: created.Application.Id.ToString()), ct);
        await externalProviders.RefreshProjectAsync(projectId, ct);
        return new ApplicationCreationResult(created.Application, created.NewClientSecret!);
    }

    /// <inheritdoc />
    public async Task<ClientApplicationInfo> UpdateAsync(Guid applicationId, ApplicationSpec spec, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        Validate(spec, requireName: false);
        var current = await db.Set<Auth2Application>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, ct)
            ?? throw new ProjectNotFoundException("Application not found.");
        if (!string.IsNullOrWhiteSpace(spec.Name)
            && await db.Set<Auth2Application>().AnyAsync(a => a.ProjectId == current.ProjectId && a.Id != applicationId && a.Name == spec.Name.Trim(), ct))
            throw new InvalidOperationException($"The project already has an application named '{spec.Name.Trim()}'.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == current.ProjectId, ct);

        var info = await provisioner.UpdateAsync(applicationId, project.Settings, spec, null, clock.GetUtcNow(), ct);
        statusCache.InvalidateApplication(info.ClientId);
        await audit.WriteAsync(new AuditEntry("application.updated", project.Id, TargetType: "application", TargetId: applicationId.ToString(),
            Data: new Dictionary<string, object?> { ["settingsVersion"] = info.SettingsVersion }), ct);
        return info;
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(Guid applicationId, bool enabled, string? reason, Guid? actorUserId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var current = await db.Set<Auth2Application>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, ct)
            ?? throw new ProjectNotFoundException("Application not found.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == current.ProjectId, ct);
        if (project.IsGlobalProject && !enabled && current.ClientId == project.ProjectKey)
            throw new InvalidOperationException("The _global application cannot be disabled.");
        if (current.IsEnabled == enabled) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await provisioner.UpdateAsync(applicationId, project.Settings, new ApplicationSpec(""), enabled, clock.GetUtcNow(), ct);
        var revoked = enabled ? 0 : await provisioner.RevokeAsync(project.Id, applicationId, ct);
        await audit.WriteAsync(new AuditEntry(enabled ? "application.enabled" : "application.disabled", project.Id, ActorUserId: actorUserId,
            TargetType: "application", TargetId: applicationId.ToString(), Data: new Dictionary<string, object?> { ["reason"] = reason, ["revoked"] = revoked }), ct);
        await tx.CommitAsync(ct);
        statusCache.InvalidateApplication(current.ClientId!);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid applicationId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var current = await db.Set<Auth2Application>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (current is null) return;
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == current.ProjectId, ct);
        if (project.IsGlobalProject && current.ClientId == project.ProjectKey)
            throw new InvalidOperationException("The _global application cannot be deleted.");
        await provisioner.DeleteAsync(applicationId, ct);
        statusCache.InvalidateApplication(current.ClientId!);
        await audit.WriteAsync(new AuditEntry("application.deleted", project.Id, TargetType: "application", TargetId: applicationId.ToString()), ct);
    }

    /// <inheritdoc />
    public async Task<string> RotateSecretAsync(string clientId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var app = await db.Set<Auth2Application>().AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == clientId, ct)
            ?? throw new ProjectNotFoundException($"No application with client id '{clientId}'.");
        var secret = await provisioner.RotateSecretAsync(clientId, ct);
        await audit.WriteAsync(new AuditEntry("application.secret_rotated", app.ProjectId, TargetType: "application", TargetId: app.Id.ToString()), ct);
        return secret;
    }

    /// <inheritdoc />
    public async Task<ClientApplicationInfo?> FindAsync(Guid applicationId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var a = await db.Set<Auth2Application>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == applicationId, ct);
        return a is null ? null : OpenIddictClientProvisioner.ToInfo(a);
    }

    /// <inheritdoc />
    public async Task<ClientApplicationInfo?> FindByClientIdAsync(string clientId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var a = await db.Set<Auth2Application>().AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == clientId, ct);
        return a is null ? null : OpenIddictClientProvisioner.ToInfo(a);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClientApplicationInfo>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var rows = await db.Set<Auth2Application>().AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Name).ToListAsync(ct);
        return [.. rows.Select(OpenIddictClientProvisioner.ToInfo)];
    }

    private static void Validate(ApplicationSpec spec, bool requireName)
    {
        if (requireName && string.IsNullOrWhiteSpace(spec.Name)) throw new ArgumentException("An application needs a name.", nameof(spec));
        var errors = new List<SettingsError>();
        ProjectSettingsValidator.CheckUris("RedirectUris", spec.RedirectUris ?? [], (p, m) => errors.Add(new SettingsError(p, m)));
        ProjectSettingsValidator.CheckUris("PostLogoutRedirectUris", spec.PostLogoutRedirectUris ?? [], (p, m) => errors.Add(new SettingsError(p, m)));
        if (spec.Settings is { } s) ClientApplicationSettingsValidator.Validate(s, (p, m) => errors.Add(new SettingsError(p, m)));
        if (errors.Count > 0) throw new SettingsValidationException(errors);
    }
}
