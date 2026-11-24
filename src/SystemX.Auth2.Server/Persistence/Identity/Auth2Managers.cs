using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Persistence.Identity;

/// <summary>
/// User manager. Thin: project-based rules come from <see cref="IAuthFlowResolver"/>.
/// It only replaces the lockout rules, because <c>IdentityOptions</c> are global and cannot change per request.
/// </summary>
public class Auth2UserManager(
    IUserStore<AppUser> store, IOptions<IdentityOptions> optionsAccessor, IPasswordHasher<AppUser> passwordHasher,
    IEnumerable<IUserValidator<AppUser>> userValidators, IEnumerable<IPasswordValidator<AppUser>> passwordValidators,
    ILookupNormalizer keyNormalizer, IdentityErrorDescriber errors, IServiceProvider services, ILogger<UserManager<AppUser>> logger,
    IAuthFlowResolver flow, TimeProvider clock)
    : UserManager<AppUser>(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
{
    private IUserLockoutStore<AppUser> LockoutStore => (IUserLockoutStore<AppUser>)Store;

    /// <inheritdoc />
    public override async Task<IdentityResult> CreateAsync(AppUser user)
    {
        var lockout = (await flow.GetSettingsAsync(CancellationToken)).Lockout;
        await LockoutStore.SetLockoutEnabledAsync(user, lockout.LockoutEnabled && lockout.LockoutForNewUsers, CancellationToken);
        return await base.CreateAsync(user);
    }

    /// <inheritdoc />
    public override async Task<bool> GetLockoutEnabledAsync(AppUser user) =>
        (await flow.GetSettingsAsync(CancellationToken)).Lockout.LockoutEnabled && await base.GetLockoutEnabledAsync(user);

    /// <inheritdoc />
    public override async Task<IdentityResult> AccessFailedAsync(AppUser user)
    {
        var rules = (await flow.GetSettingsAsync(CancellationToken)).Lockout;
        if (!rules.LockoutEnabled || !await LockoutStore.GetLockoutEnabledAsync(user, CancellationToken)) return IdentityResult.Success;

        var count = await LockoutStore.IncrementAccessFailedCountAsync(user, CancellationToken);
        if (count >= rules.MaxFailedAttempts)
        {
            await LockoutStore.SetLockoutEndDateAsync(user, clock.GetUtcNow().AddMinutes(rules.LockoutMinutes), CancellationToken);
            await LockoutStore.ResetAccessFailedCountAsync(user, CancellationToken);
        }
        return await UpdateUserAsync(user);
    }
}

/// <summary>Role manager. Thin.</summary>
public class Auth2RoleManager(
    IRoleStore<AppRole> store, IEnumerable<IRoleValidator<AppRole>> roleValidators, ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors, ILogger<RoleManager<AppRole>> logger)
    : RoleManager<AppRole>(store, roleValidators, keyNormalizer, errors, logger);

/// <summary>Sign-in manager. Thin. Login rules live in <c>ILoginService</c>.</summary>
public class Auth2SignInManager(
    UserManager<AppUser> userManager, IHttpContextAccessor contextAccessor, IUserClaimsPrincipalFactory<AppUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor, ILogger<SignInManager<AppUser>> logger, IAuthenticationSchemeProvider schemes,
    IUserConfirmation<AppUser> confirmation)
    : SignInManager<AppUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation);
