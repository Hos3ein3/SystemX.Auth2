using System.Collections.Immutable;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

internal sealed class TwoFactorService(
    UserManager<AppUser> users, IAuthFlowResolver flow, ICredentialStateProvider credentials, IAccessVersionService versions, IAuditService audit,
    IReplayCache replay, TimeProvider clock) : ITwoFactorService
{
    public async Task<(OperationResult, TwoFactorSetupResponse?)> SetupAsync(AppUser user, CancellationToken ct = default)
    {
        var settings = await flow.GetSettingsAsync(ct);
        if (!settings.TwoFactor.AllowTotp) return (OperationResult.Fail(Auth2ErrorCodes.TwoFactorNotAllowed), null);
        if (await users.GetTwoFactorEnabledAsync(user)) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);

        await users.ResetAuthenticatorKeyAsync(user);   // a new key each time until it is turned on
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        var app = await flow.GetProjectAsync(ct);
        var issuer = Uri.EscapeDataString(settings.Localization.BrandName ?? app.Name);
        var label = Uri.EscapeDataString(user.Email ?? user.UserName ?? user.Id.ToString("N"));
        var uri = $"otpauth://totp/{issuer}:{label}?secret={key}&issuer={issuer}&digits=6";
        return (OperationResult.Ok, new TwoFactorSetupResponse(string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)))).ToLowerInvariant(), uri));
    }

    public async Task<OperationResult> VerifyAsync(AppUser user, string code, bool optional = false, CancellationToken ct = default)
    {
        var settings = await flow.GetSettingsAsync(ct);
        var app = await flow.GetProjectAsync(ct);
        // No authenticator app is possible, or the person has none: an optional action has nothing to ask, a required one tells the person to set it up.
        if (!settings.TwoFactor.AllowTotp || !await users.GetTwoFactorEnabledAsync(user))
            return optional ? OperationResult.Ok : OperationResult.Fail(Auth2ErrorCodes.TotpSetupRequired);
        if (await users.IsLockedOutAsync(user)) return OperationResult.Fail(Auth2ErrorCodes.LockedOut);

        var normalized = Normalize(code);
        if (normalized.Length == 0) return OperationResult.Fail(Auth2ErrorCodes.TotpRequired);   // no code was given: ask for one (not a wrong guess, so not counted)
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, normalized))
        {
            await users.AccessFailedAsync(user);   // guesses count towards the lockout
            await audit.WriteAsync(new AuditEntry("2fa.step_up_failed", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
            return OperationResult.Fail(Auth2ErrorCodes.InvalidOtp);
        }

        // A code is valid for about 90 seconds. It may be used once, so a copied or intercepted code is of no use to anybody else.
        if (!await replay.TryAddAsync(app.Id, $"totp:{user.Id:N}:{normalized}", clock.GetUtcNow().AddMinutes(3), ct))
        {
            await audit.WriteAsync(new AuditEntry("2fa.step_up_replayed", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
            return OperationResult.Fail(Auth2ErrorCodes.InvalidOtp);
        }

        await users.ResetAccessFailedCountAsync(user);
        await audit.WriteAsync(new AuditEntry("2fa.step_up", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<(OperationResult, IReadOnlyList<string>)> EnableAsync(AppUser user, string code, CancellationToken ct = default)
    {
        var settings = await flow.GetSettingsAsync(ct);
        var app = await flow.GetProjectAsync(ct);
        if (!settings.TwoFactor.AllowTotp) return (OperationResult.Fail(Auth2ErrorCodes.TwoFactorNotAllowed), []);
        if (await users.GetTwoFactorEnabledAsync(user) || await users.GetAuthenticatorKeyAsync(user) is null) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), []);
        if (await users.IsLockedOutAsync(user)) return (OperationResult.Fail(Auth2ErrorCodes.LockedOut), []);

        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, Normalize(code)))
        {
            await users.AccessFailedAsync(user);
            return (OperationResult.Fail(Auth2ErrorCodes.InvalidOtp), []);
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        IReadOnlyList<string> codes = settings.TwoFactor.AllowRecoveryCodes
            ? [.. (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, settings.TwoFactor.RecoveryCodeCount)) ?? []]
            : [];
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("2fa.enabled", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return (OperationResult.Ok, codes);
    }

    public async Task<OperationResult> DisableAsync(AppUser user, string password, CancellationToken ct = default)
    {
        var settings = await flow.GetSettingsAsync(ct);
        var app = await flow.GetProjectAsync(ct);
        if (settings.TwoFactor.Mode == TwoFactorMode.Required) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        if (!await users.GetTwoFactorEnabledAsync(user)) return OperationResult.Fail(Auth2ErrorCodes.TwoFactorNotEnabled);
        if (await users.IsLockedOutAsync(user)) return OperationResult.Fail(Auth2ErrorCodes.LockedOut);
        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return OperationResult.Fail(Auth2ErrorCodes.InvalidCredentials);
        }

        var state = await credentials.GetAsync(ct);
        await state.SetTwoFactorEnabledAsync(user, false, ct);
        await state.SetAuthenticatorKeyAsync(user, null, ct);
        await state.ReplaceRecoveryCodesAsync(user, [], ct);
        var saved = await users.UpdateAsync(user);   // saves the changes of the credential row
        if (!saved.Succeeded) return OperationResult.Fail(saved.Errors);
        await users.UpdateSecurityStampAsync(user);
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("2fa.disabled", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<(OperationResult, IReadOnlyList<string>)> RegenerateRecoveryCodesAsync(AppUser user, string code, CancellationToken ct = default)
    {
        var settings = await flow.GetSettingsAsync(ct);
        var app = await flow.GetProjectAsync(ct);
        if (!settings.TwoFactor.AllowRecoveryCodes) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), []);
        if (!await users.GetTwoFactorEnabledAsync(user)) return (OperationResult.Fail(Auth2ErrorCodes.TwoFactorNotEnabled), []);
        if (await users.IsLockedOutAsync(user)) return (OperationResult.Fail(Auth2ErrorCodes.LockedOut), []);
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, Normalize(code)))
        {
            await users.AccessFailedAsync(user);
            return (OperationResult.Fail(Auth2ErrorCodes.InvalidOtp), []);
        }
        IReadOnlyList<string> codes = [.. (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, settings.TwoFactor.RecoveryCodeCount)) ?? []];
        await audit.WriteAsync(new AuditEntry("2fa.recovery_codes_regenerated", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return (OperationResult.Ok, codes);
    }

    private static string Normalize(string code) => (code ?? "").Replace(" ", "").Replace("-", "");
}

internal sealed class AccessVersionService(Auth2DbContext db, IAuthFlowResolver flow, IOptions<Auth2ServerOptions> options) : IAccessVersionService
{
    public async Task BumpUserAsync(AppUser user, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        if (options.Value.Mode == ServerMode.SingleProject)
        {
            await BumpProjectAsync(ct);   // no user_applications rows in single mode
            return;
        }
        await db.UserProjects.Where(x => x.UserId == user.Id && x.ProjectId == app.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserAccessVersion, x => x.UserAccessVersion + 1), ct);
    }

    public async Task BumpProjectAsync(CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        await db.Projects.Where(a => a.Id == app.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.AccessVersion, a => a.AccessVersion + 1), ct);
    }
}

internal sealed class ConsentService(IOpenIddictAuthorizationManager authorizations, IDataProtectionProvider protection) : IConsentService
{
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(10);
    private ITimeLimitedDataProtector Protector => protection.CreateProtector("SystemX.Auth2.ConsentGrant").ToTimeLimitedDataProtector();

    public async Task<bool> IsConsentNeededAsync(AppUser user, ClientApplicationInfo app, EffectiveSettings settings, IReadOnlyCollection<string> scopes, string? grantToken, CancellationToken ct = default)
    {
        var mode = settings.ConsentMode;
        if (mode == ConsentMode.Implicit) return false;
        if (IsGrantValid(grantToken, user, app, scopes)) return false;
        return mode == ConsentMode.Systematic || await FindAuthorizationIdAsync(user, app, scopes, ct) is null;
    }

    public string CreateGrantToken(AppUser user, ClientApplicationInfo app, IEnumerable<string> scopes) =>
        Protector.Protect($"{user.Id:N}|{app.Id:N}|{Hash(scopes)}", GrantLifetime);

    public async Task<string?> FindAuthorizationIdAsync(AppUser user, ClientApplicationInfo app, IReadOnlyCollection<string> scopes, CancellationToken ct = default)
    {
        await foreach (var a in authorizations.FindAsync(user.Id.ToString("D"), app.Id.ToString("D"), OpenIddictConstants.Statuses.Valid,
            OpenIddictConstants.AuthorizationTypes.Permanent, [.. scopes], ct))
            return await authorizations.GetIdAsync(a, ct);
        return null;
    }

    public async Task<string> SaveAuthorizationAsync(AppUser user, ClientApplicationInfo app, ClaimsPrincipal principal, IReadOnlyCollection<string> scopes, CancellationToken ct = default)
    {
        var created = await authorizations.CreateAsync(principal, user.Id.ToString("D"), app.Id.ToString("D"),
            OpenIddictConstants.AuthorizationTypes.Permanent, [.. scopes], ct);
        return (await authorizations.GetIdAsync(created, ct))!;
    }

    private bool IsGrantValid(string? token, AppUser user, ClientApplicationInfo app, IEnumerable<string> scopes)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try { return Protector.Unprotect(token) == $"{user.Id:N}|{app.Id:N}|{Hash(scopes)}"; }
        catch (CryptographicException) { return false; }
    }

    private static string Hash(IEnumerable<string> scopes) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(' ', scopes.Distinct().OrderBy(s => s, StringComparer.Ordinal)))));
}
