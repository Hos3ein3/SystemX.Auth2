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
    IProjectContext applications,
    IProjectScope scope,
    IOptions<Auth2ServerOptions> options,
    IPasswordHistoryService passwordHistory,
    PasswordRehashMarker rehash,
    TimeProvider clock,
    IdentityErrorDescriber? describer = null)
    : UserStore<AppUser, AppRole, Auth2DbContext, Guid, IdentityUserClaim<Guid>, IdentityUserRole<Guid>, AppUserLogin, AppUserToken, IdentityRoleClaim<Guid>, AppUserPasskey>(context, describer)
{
    private Task<ICredentialState> State(CancellationToken ct) => credentials.GetAsync(ct);

    // ---- credentials
    /// <inheritdoc />
    public override async Task<string?> GetPasswordHashAsync(AppUser user, CancellationToken ct = default) => await (await State(ct)).GetPasswordHashAsync(user, ct);
    /// <summary>
    /// Sets the password hash. When a password is **replaced** (change, reset, set by an admin) the old hash is handed to <see cref="IPasswordHistoryService"/>
    /// and the date of the password is renewed. A rehash (the same password stored with a stronger hash while it is checked) is neither: it keeps the date.
    /// </summary>
    public override async Task SetPasswordHashAsync(AppUser user, string? passwordHash, CancellationToken ct = default)
    {
        var state = await State(ct);
        if (rehash.Active) { await state.SetPasswordHashAsync(user, passwordHash, ct); return; }

        var old = await state.GetPasswordHashAsync(user, ct);
        if (old is not null && passwordHash is not null && old != passwordHash) await passwordHistory.RememberAsync(user, old, ct);
        await state.SetPasswordHashAsync(user, passwordHash, ct);
        if (passwordHash is not null) await state.SetPasswordChangedAtAsync(user, clock.GetUtcNow(), ct);
    }
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

    // ---- external logins and passkeys belong to the current application

    private async Task<Guid> CurrentProjectIdAsync(CancellationToken ct) => (await applications.GetRequiredAsync(ct)).Id;

    /// <inheritdoc />
    public override async Task AddLoginAsync(AppUser user, UserLoginInfo login, CancellationToken ct = default)
    {
        await applications.GetRequiredAsync(ct);
        await base.AddLoginAsync(user, login, ct);
    }

    /// <inheritdoc />
    protected override AppUserLogin CreateUserLogin(AppUser user, UserLoginInfo login) => new()
    {
        UserId = user.Id,
        ProjectId = scope.ProjectId ?? throw new InvalidOperationException("A login needs an application."),
        ProviderKey = login.ProviderKey,
        LoginProvider = login.LoginProvider,
        ProviderDisplayName = login.ProviderDisplayName,
    };

    /// <inheritdoc />
    public override async Task RemoveLoginAsync(AppUser user, string loginProvider, string providerKey, CancellationToken ct = default)
    {
        await applications.GetRequiredAsync(ct);
        await base.RemoveLoginAsync(user, loginProvider, providerKey, ct);
    }

    /// <inheritdoc />
    public override async Task<IList<UserLoginInfo>> GetLoginsAsync(AppUser user, CancellationToken ct = default)
    {
        await applications.GetRequiredAsync(ct);
        return await base.GetLoginsAsync(user, ct);
    }

    /// <inheritdoc />
    public override async Task<AppUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken ct = default)
    {
        await applications.GetRequiredAsync(ct);
        return await base.FindByLoginAsync(loginProvider, providerKey, ct);
    }

    /// <inheritdoc />
    protected override AppUserPasskey CreateUserPasskey(AppUser user, UserPasskeyInfo passkey)
    {
        var p = base.CreateUserPasskey(user, passkey);
        p.ProjectId = scope.ProjectId ?? throw new InvalidOperationException("A passkey needs an application.");
        return p;
    }

    /// <inheritdoc />
    public override async Task<AppUser?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken ct)
    {
        await applications.GetRequiredAsync(ct);
        return await base.FindByPasskeyIdAsync(credentialId, ct);
    }

    /// <inheritdoc />
    public override async Task<UserPasskeyInfo?> FindPasskeyAsync(AppUser user, byte[] credentialId, CancellationToken ct)
    {
        await applications.GetRequiredAsync(ct);
        return await base.FindPasskeyAsync(user, credentialId, ct);
    }

    /// <inheritdoc />
    public override async Task AddOrUpdatePasskeyAsync(AppUser user, UserPasskeyInfo passkey, CancellationToken ct)
    {
        await applications.GetRequiredAsync(ct);
        await base.AddOrUpdatePasskeyAsync(user, passkey, ct);
    }

    /// <inheritdoc />
    public override async Task RemovePasskeyAsync(AppUser user, byte[] credentialId, CancellationToken ct)
    {
        await applications.GetRequiredAsync(ct);
        await base.RemovePasskeyAsync(user, credentialId, ct);
    }

    /// <inheritdoc />
    public override async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(AppUser user, CancellationToken ct)
    {
        await applications.GetRequiredAsync(ct);
        return await base.GetPasskeysAsync(user, ct);
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
            if (options.Value.Mode == ServerMode.SingleProject)
                await Context.Projects.Where(a => a.Id == app.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.AccessVersion, a => a.AccessVersion + 1), ct);
            else
                await Context.UserProjects.Where(x => x.UserId == user.Id && x.ProjectId == app.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserAccessVersion, x => x.UserAccessVersion + 1), ct);
        }
        return result;
    }

    /// <summary>Finds a role by normalized name. A role of the current application wins over a global role with the same name.</summary>
    protected override async Task<AppRole?> FindRoleAsync(string normalizedRoleName, CancellationToken ct)
    {
        await RoleScopeHelper.EnsureProjectAsync(applications, scope, ct);
        var current = scope.ProjectId;
        return await Context.Roles.Where(r => r.NormalizedName == normalizedRoleName)
            .OrderBy(r => r.ProjectId == current ? 0 : r.ProjectId == null ? 1 : 2)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Roles of the user in the current application. Global roles (for example <c>GlobalAdmin</c>) are included only
    /// in the <c>_global</c> project, and in single-project mode.
    /// </summary>
    public override async Task<IList<string>> GetRolesAsync(AppUser user, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var app = await applications.GetRequiredAsync(ct);
        var includeGlobal = app.IsGlobalProject || options.Value.Mode == ServerMode.SingleProject;
        return await (from ur in Context.UserRoles
                      join r in Context.Roles on ur.RoleId equals r.Id
                      where ur.UserId == user.Id && (r.ProjectId == app.Id || (includeGlobal && r.ProjectId == null))
                      select r.Name!).ToListAsync(ct);
    }
}
