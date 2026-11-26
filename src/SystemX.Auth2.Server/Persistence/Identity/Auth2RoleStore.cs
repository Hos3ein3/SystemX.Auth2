using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Persistence.Identity;

/// <summary>
/// Role store. Finds roles by name inside the current application (plus global roles).
/// A role of the current application wins over a global role with the same name.
/// Roles are created with the <c>ProjectId</c> you set. A role with no <c>ProjectId</c> is a global role.
/// </summary>
public class Auth2RoleStore(Auth2DbContext context, IProjectScope scope, IProjectContext applications, IdentityErrorDescriber? describer = null)
    : RoleStore<AppRole, Auth2DbContext, Guid, IdentityUserRole<Guid>, IdentityRoleClaim<Guid>>(context, describer)
{
    /// <inheritdoc />
    public override async Task<AppRole?> FindByNameAsync(string normalizedName, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await RoleScopeHelper.EnsureProjectAsync(applications, scope, ct);
        var current = scope.ProjectId;
        return await Context.Roles.Where(r => r.NormalizedName == normalizedName)
            .OrderBy(r => r.ProjectId == current ? 0 : r.ProjectId == null ? 1 : 2)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>The built-in roles <c>GlobalAdmin</c> and <c>Developer</c> are never deleted, by any code path (the admin pages, the API, a sync, or code that uses <c>RoleManager</c> directly).</summary>
    public override Task<IdentityResult> DeleteAsync(AppRole role, CancellationToken ct = default) =>
        PrivilegedRoles.IsPrivileged(role.Name)
            ? Task.FromResult(IdentityResult.Failed(new IdentityError { Code = Contracts.Constants.Auth2ErrorCodes.Forbidden, Description = "The roles GlobalAdmin and Developer cannot be deleted." }))
            : base.DeleteAsync(role, ct);
}

/// <summary>Role name must be unique inside the same application only (<c>writer</c> in two applications is fine).</summary>
public sealed class Auth2RoleValidator(Auth2DbContext db, IdentityErrorDescriber describer) : IRoleValidator<AppRole>
{
    /// <inheritdoc />
    public async Task<IdentityResult> ValidateAsync(RoleManager<AppRole> manager, AppRole role)
    {
        if (string.IsNullOrWhiteSpace(role.Name)) return IdentityResult.Failed(describer.InvalidRoleName(role.Name));
        // The names GlobalAdmin and Developer belong to the two built-in global roles. A role of a project (or a renamed role) may not take them:
        // the Server reads these roles by name, so a project role with such a name would be treated as the real one.
        if (PrivilegedRoles.IsPrivileged(role.Name) && role.ProjectId is not null) return IdentityResult.Failed(describer.InvalidRoleName(role.Name));
        var normalized = manager.NormalizeKey(role.Name);
        // Looks at every application on purpose: the same-application rule must hold even when a global scope is active.
        var duplicate = await db.Roles.IgnoreQueryFilters()
            .AnyAsync(r => r.NormalizedName == normalized && r.ProjectId == role.ProjectId && r.Id != role.Id);
        return duplicate ? IdentityResult.Failed(describer.DuplicateRoleName(role.Name)) : IdentityResult.Success;
    }
}

/// <summary>Makes sure the current application is resolved before a role lookup, so the query filter and the ordering are right.</summary>
internal static class RoleScopeHelper
{
    public static async Task EnsureProjectAsync(IProjectContext applications, IProjectScope scope, CancellationToken ct)
    {
        if (scope.ProjectId is not null) return;
        try { await applications.GetRequiredAsync(ct); }
        catch (ProjectNotFoundException) when (scope.IsGlobalActive) { /* developer code with no application: global lookup */ }
    }
}
