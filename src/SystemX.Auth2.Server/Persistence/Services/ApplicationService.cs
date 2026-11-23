using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Persistence.Services;

/// <summary>Creates applications and updates their settings.</summary>
public sealed class ApplicationService(
    Auth2DbContext db,
    IGlobalScope globalScope,
    IClientProvisioner provisioner,
    IApplicationSettingsProvider settingsProvider,
    IAuditService audit,
    IOptions<Auth2ServerOptions> options,
    TimeProvider clock) : IApplicationService
{
    /// <inheritdoc />
    public async Task<Auth2Application> CreateAsync(string projectKey, string name, string? description, string? clientId, CancellationToken ct = default)
    {
        if (options.Value.Mode == ServerMode.SingleApplication)
            throw new InvalidOperationException("The server is single-application. It has exactly one application, created by the seeder.");
        if (!ProjectKeyRules.IsValidForNewApplication(projectKey))
            throw new ArgumentException("ProjectKey must use lowercase letters, digits, '-' or '_', be 3-64 characters long and must not be '_global'.", nameof(projectKey));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));

        using var _ = globalScope.Begin();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var app = new Auth2Application
        {
            ProjectKey = projectKey,
            Name = name.Trim(),
            Description = description,
            ClientId = string.IsNullOrWhiteSpace(clientId) ? projectKey : clientId.Trim(),
            CreatedAtUtc = clock.GetUtcNow(),
        };
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        app.OpenIddictApplicationId = await provisioner.EnsureClientAsync(app, ct);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("application.created", app.Id, TargetType: "application", TargetId: app.Id.ToString()), ct);
        await tx.CommitAsync(ct);
        return app;
    }

    /// <inheritdoc />
    public async Task<Auth2Application?> FindByProjectKeyAsync(string projectKey, CancellationToken ct = default) =>
        await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectKey == projectKey, ct);

    /// <inheritdoc />
    public async Task UpdateSettingsAsync(Guid applicationId, ApplicationSettings settings, CancellationToken ct = default)
    {
        ApplicationSettingsValidator.ValidateOrThrow(settings);
        using var _ = globalScope.Begin();
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, ct)
            ?? throw new ApplicationNotFoundException("Application not found.");
        app.Settings = settings;
        app.SettingsVersion++;
        // The value comparer sees no change when the JSON is identical, so we force the property state.
        db.Entry(app).Property(a => a.Settings).IsModified = true;
        await db.SaveChangesAsync(ct);
        settingsProvider.Invalidate(applicationId);
        await audit.WriteAsync(new AuditEntry("application.settings_updated", applicationId, TargetType: "application",
            TargetId: applicationId.ToString(), Data: new Dictionary<string, object?> { ["settingsVersion"] = app.SettingsVersion }), ct);
    }
}
