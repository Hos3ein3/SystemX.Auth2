using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Persistence.Identity;

/// <summary>
/// User store that keeps credentials where the credential scope says (<see cref="ICredentialState"/>)
/// and looks up roles inside the current application.
/// </summary>
public class Auth2UserStore(
    Auth2DbContext context,
    ICredentialStateProvider credentials,
    IApplicationContext applications,
    IApplicationScope scope,
    IOptions<Auth2ServerOptions> options,
    IdentityErrorDescriber? describer = null)
    : UserStore<AppUser, AppRole, Auth2DbContext, Guid, IdentityUserClaim<Guid>, IdentityUserRole<Guid>, AppUserLogin, AppUserToken, IdentityRoleClaim<Guid>>(context, describer)
{
    private Task<ICredentialState> State(CancellationToken ct) => credentials.GetAsync(ct);

    // ---- credentials
    /// <inheritdoc />
    public override async Task<string?> GetPasswordHashAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).GetPasswordHashAsync(user, ct);
    /// <inheritdoc />
    public override async Task SetPasswordHashAsync(AppUser user, string? passwordHash, CancellationToken ct = default) => await (await State(ct)).SetPasswordHashAsync(user, passwordHash, ct);
    /// <inheritdoc />
    public override async Task<bool> HasPasswordAsync(AppUser user, CancellationToken ct = default) => await GetPasswordHashAsync(user, ct) is not null;
    /// <inheritdoc />
    public override async Task<string?> GetSecurityStampAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).GetSecurityStampAsync(user, ct);
    /// <inheritdoc />
    public override async Task SetSecurityStampAsync(AppUser user, string stamp, CancellationToken ct = default) => await (await State(ct)).SetSecurityStampAsync(user, stamp, ct);
    /// <inheritdoc />
    public override async Task<bool> GetTwoFactorEnabledAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).GetTwoFactorEnabledAsync(user, ct);
    /// <inheritdoc />
    public override async Task SetTwoFactorEnabledAsync(AppUser user, bool enabled, CancellationToken ct = default) => await (await State(ct)).SetTwoFactorEnabledAsync(user, enabled, ct);
    /// <inheritdoc />
    public override async Task<DateTimeOffset?> GetLockoutEndDateAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).GetLockoutEndAsync(user, ct);
    /// <inheritdoc />
    public override async Task SetLockoutEndDateAsync(AppUser user, DateTimeOffset? lockoutEnd, CancellationToken ct = default) => await (await State(ct)).SetLockoutEndAsync(user, lockoutEnd, ct);
    /// <inheritdoc />
    public override async Task<int> GetAccessFailedCountAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).GetAccessFailedCountAsync(user, ct);
    /// <inheritdoc />
    public override async Task<int> IncrementAccessFailedCountAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).IncrementAccessFailedCountAsync(user, ct);
    /// <inheritdoc />
    public override async Task ResetAccessFailedCountAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).ResetAccessFailedCountAsync(user, ct);
    /// <inheritdoc />
    public override async Task<bool> GetLockoutEnabledAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).GetLockoutEnabledAsync(user, ct);
    /// <inheritdoc />
    public override async Task SetLockoutEnabledAsync(AppUser user, bool enabled, CancellationToken ct = default) => await (await State(ct)).SetLockoutEnabledAsync(user, enabled, ct);
    /// <inheritdoc />
    public override async Task SetAuthenticatorKeyAsync(AppUser user, string key, CancellationToken ct) => await (await State(ct)).SetAuthenticatorKeyAsync(user, key, ct);
    /// <inheritdoc />
    public override async Task<string?> GetAuthenticatorKeyAsync(AppUser user, CancellationToken ct) => await (await State(ct)).GetAuthenticatorKeyAsync(user, ct);
    /// <inheritdoc />
    public override async Task ReplaceCodesAsync(AppUser user, IEnumerable<string> recoveryCodes, CancellationToken ct) => await (await State(ct)).ReplaceRecoveryCodesAsync(user, recoveryCodes, ct);
    /// <inheritdoc />
    public override async Task<bool> RedeemCodeAsync(AppUser user, string code, CancellationToken ct) => await (await State(ct)).RedeemRecoveryCodeAsync(user, code, ct);
    /// <inheritdoc />
    public override async Task<int> CountCodesAsync(AppUser user, CancellationToken ct) => await (await State(ct)).CountRecoveryCodesAsync(user, ct);

    /// <inheritdoc />
    public override async Task<IdentityResult> CreateAsync(AppUser user, CancellationToken ct = default)
    {
        (await State(ct)).BeforeUserCreated(user);
        return await base.CreateAsync(user, ct);
    }

    // ---- roles are looked up inside the current application

    private bool rolesChanged;

    /// <inheritdoc />
    public override async Task AddToRoleAsync(AppUser user, string normalizedRoleName, CancellationToken ct = default)
    {
        await base.AddToRoleAsync(user, normalizedRoleName, ct);
        rolesChanged = true;
    }

    /// <inheritdoc />
    public override async Task RemoveFromRoleAsync(AppUser user, string normalizedRoleName, CancellationToken ct = default)
    {
        await base.RemoveFromRoleAsync(user, normalizedRoleName, ct);
        rolesChanged = true;
    }

    /// <summary>
    /// Saves the user. When roles were added or removed through this store (by anyone, not only by <c>IAccessService</c>),
    /// the access version is raised after the save, so caches and the <c>pv</c> claim never go stale.
    /// </summary>
    public override async Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        var result = await base.UpdateAsync(user, ct);
        if (result.Succeeded && rolesChanged)
        {
            rolesChanged = false;
            var app = await applications.GetRequiredAsync(ct);
            if (options.Value.Mode == ServerMode.SingleApplication)
                await Context.Applications.Where(a => a.Id == app.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.AccessVersion, a => a.AccessVersion + 1), ct);
            else
                await Context.UserApplications.Where(x => x.UserId == user.Id && x.ApplicationId == app.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserAccessVersion, x => x.UserAccessVersion + 1), ct);
        }
        return result;
    }

    /// <summary>Finds a role by normalized name. A role of the current application wins over a global role with the same name.</summary>
    protected override async Task<AppRole?> FindRoleAsync(string normalizedRoleName, CancellationToken ct)
    {
        await RoleScopeHelper.EnsureApplicationAsync(applications, scope, ct);
        var current = scope.ApplicationId;
        return await Context.Roles.Where(r => r.NormalizedName == normalizedRoleName)
            .OrderBy(r => r.ApplicationId == current ? 0 : r.ApplicationId == null ? 1 : 2)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Roles of the user in the current application. Global roles (for example <c>GlobalAdmin</c>) are included only
    /// in the <c>_global</c> project, and in single-application mode.
    /// </summary>
    public override async Task<IList<string>> GetRolesAsync(AppUser user, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var app = await applications.GetRequiredAsync(ct);
        var includeGlobal = app.IsGlobalProject || options.Value.Mode == ServerMode.SingleApplication;
        return await (from ur in Context.UserRoles
                      join r in Context.Roles on ur.RoleId equals r.Id
                      where ur.UserId == user.Id && (r.ApplicationId == app.Id || (includeGlobal && r.ApplicationId == null))
                      select r.Name!).ToListAsync(ct);
    }
}
