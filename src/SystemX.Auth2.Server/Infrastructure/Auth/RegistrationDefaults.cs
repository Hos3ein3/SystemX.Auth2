using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>See <see cref="IRegistrationDefaults"/>.</summary>
internal sealed class RegistrationDefaults(IAuthFlowResolver flow, IAccessService access, IGroupService groups, IAuditService audit, ILogger<RegistrationDefaults> log) : IRegistrationDefaults
{
    public async Task ApplyAsync(AppUser user, CancellationToken ct = default)
    {
        var project = await flow.GetProjectAsync(ct);
        var registration = (await flow.GetSettingsAsync(ct)).Registration;
        if (registration.AssignedRoles.Count + registration.AssignedGroups.Count + registration.AssignedPermissions.Count == 0) return;

        var done = new List<string>();
        var skipped = new List<string>();

        foreach (var role in registration.AssignedRoles)
        {
            // Checked again here: settings written before this rule, or by hand, must not hand out an admin role.
            if (ProjectSettingsValidator.ReservedRoles.Contains(role, StringComparer.OrdinalIgnoreCase)) { skipped.Add($"role:{role}"); continue; }
            (await access.AssignRoleAsync(user, role, ct) is { Succeeded: true } ? done : skipped).Add($"role:{role}");
        }
        foreach (var group in registration.AssignedGroups)
            (await groups.AddUserAsync(group, user, ct) is { Succeeded: true } ? done : skipped).Add($"group:{group}");
        foreach (var permission in registration.AssignedPermissions)
            (await access.GrantPermissionAsync(user, permission, null, ct) is { Succeeded: true } ? done : skipped).Add($"permission:{permission}");

        if (skipped.Count > 0)
            log.LogWarning("Registration defaults of project {Project}: {Count} entries do not exist or could not be assigned and were skipped.", project.ProjectKey, skipped.Count);
        await audit.WriteAsync(new AuditEntry("register.defaults_applied", project.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["assigned"] = done, ["skipped"] = skipped }), ct);
    }
}
