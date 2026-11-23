using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Identity;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>See <see cref="ILoginService"/>. This class is the only place that decides if a login is valid.</summary>
public sealed class LoginService(
    UserManager<AppUser> users,
    Auth2DbContext db,
    IAuthFlowResolver flow,
    ISessionService sessions,
    IAuditService audit,
    IOptions<Auth2ServerOptions> options,
    IDataProtectionProvider protection,
    IPasswordHasher<AppUser> hasher,
    TimeProvider clock) : ILoginService
{
    private static readonly TimeSpan TwoFactorTokenLifetime = TimeSpan.FromMinutes(5);
    private const string FailedAction = "login.failed";

    // Used to spend the same time when the user does not exist (no user enumeration by timing).
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<AppUser>().HashPassword(new AppUser(), "dummy-password-for-timing"));

    private ITimeLimitedDataProtector TokenProtector => protection.CreateProtector("SystemX.Auth2.TwoFactorToken").ToTimeLimitedDataProtector();

    /// <inheritdoc />
    public async Task<LoginResult> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);

        // 1. application enabled
        if (!app.IsEnabled) return await FailAsync(app, null, Auth2ErrorCodes.ApplicationDisabled, request, ct);

        // 2. which login method is this, and is it allowed?
        var method = Classify(request.Login, settings);
        if (!settings.Methods.AllowedLoginMethods.Contains(method))
            return await FailAsync(app, null, Auth2ErrorCodes.LoginMethodNotAllowed, request, ct, new() { ["method"] = method.ToString() });

        var user = await FindUserAsync(request.Login, method, settings, ct);

        // 3. user exists, is active, and belongs to the application (multi mode). Otherwise: the same error as a wrong password.
        if (user is null || !user.IsActive)
        {
            _ = hasher.VerifyHashedPassword(new AppUser(), DummyHash.Value, request.Password);
            return await FailAsync(app, user, Auth2ErrorCodes.InvalidCredentials, request, ct, new() { ["reason"] = user is null ? "unknown_user" : "inactive_user" }, publicCode: Auth2ErrorCodes.InvalidCredentials);
        }
        if (!await IsMemberAsync(user, app, ct))
        {
            _ = hasher.VerifyHashedPassword(user, DummyHash.Value, request.Password);
            return await FailAsync(app, user, Auth2ErrorCodes.NotRegisteredInApplication, request, ct, publicCode: Auth2ErrorCodes.InvalidCredentials);
        }

        // 4. lockout
        if (await users.IsLockedOutAsync(user))
            return await FailAsync(app, user, Auth2ErrorCodes.LockedOut, request, ct);

        // 5. password
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            if (await users.IsLockedOutAsync(user))
                await audit.WriteAsync(Entry("login.locked_out", app, user, request.IpAddress, request.UserAgent), ct);
            return await FailAsync(app, user, Auth2ErrorCodes.InvalidCredentials, request, ct, new() { ["reason"] = "wrong_password" });
        }

        if (settings.Email.RequireConfirmedEmail && !user.EmailConfirmed)
            return await FailAsync(app, user, Auth2ErrorCodes.EmailNotConfirmed, request, ct);
        if (settings.Phone.RequireConfirmedPhone && !user.PhoneNumberConfirmed)
            return await FailAsync(app, user, Auth2ErrorCodes.PhoneNotConfirmed, request, ct);

        // 6. two-factor
        if (settings.TwoFactor.Mode != TwoFactorMode.Off && settings.TwoFactor.AllowTotp)
        {
            var enabled = await users.GetTwoFactorEnabledAsync(user);
            if (enabled)
            {
                return new LoginResult
                {
                    ErrorCode = Auth2ErrorCodes.TwoFactorRequired,
                    TwoFactorToken = CreateTwoFactorToken(user, app),
                    Application = app,
                };
            }
            if (settings.TwoFactor.Mode == TwoFactorMode.Required)
                return await FailAsync(app, user, Auth2ErrorCodes.TwoFactorSetupRequired, request, ct);
        }

        return await SucceedAsync(user, app, settings, ["pwd"], request.IpAddress, request.UserAgent, ct);
    }

    /// <inheritdoc />
    public async Task<LoginResult> CompleteTwoFactorAsync(string twoFactorToken, string? code, string? recoveryCode, string? ipAddress = null, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);

        var user = await ReadTwoFactorTokenAsync(twoFactorToken, app);
        if (user is null) return await FailAsync(app, null, Auth2ErrorCodes.InvalidToken, null, ct, ipAddress: ipAddress);
        if (!app.IsEnabled) return await FailAsync(app, user, Auth2ErrorCodes.ApplicationDisabled, null, ct, ipAddress: ipAddress);
        if (await users.IsLockedOutAsync(user)) return await FailAsync(app, user, Auth2ErrorCodes.LockedOut, null, ct, ipAddress: ipAddress);

        bool ok;
        var normalizedCode = (code ?? "").Replace(" ", "").Replace("-", "");
        if (!string.IsNullOrEmpty(normalizedCode))
            ok = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, normalizedCode);
        else if (!string.IsNullOrEmpty(recoveryCode) && settings.TwoFactor.AllowRecoveryCodes)
            ok = (await users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode.Trim())).Succeeded;
        else
            ok = false;

        if (!ok)
        {
            await users.AccessFailedAsync(user);
            return await FailAsync(app, user, Auth2ErrorCodes.InvalidOtp, null, ct, new() { ["step"] = "two_factor" }, ipAddress: ipAddress);
        }
        return await SucceedAsync(user, app, settings, ["pwd", "otp"], ipAddress, null, ct);
    }

    /// <inheritdoc />
    public async Task<bool> IsSubjectValidAsync(AppUser user, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        return app.IsEnabled && user.IsActive && await IsMemberAsync(user, app, ct) && !await users.IsLockedOutAsync(user);
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<LoginResult> SucceedAsync(AppUser user, Auth2Application app, ApplicationSettings settings, string[] amr, string? ip, string? ua, CancellationToken ct)
    {
        await users.ResetAccessFailedCountAsync(user);

        if (options.Value.Mode == ServerMode.MultiApplication)
        {
            var row = await db.UserApplications.FirstOrDefaultAsync(x => x.UserId == user.Id && x.ApplicationId == app.Id, ct);
            if (row is not null) { row.LastLoginUtc = clock.GetUtcNow(); await db.SaveChangesAsync(ct); }
        }

        // 7. single active session: revoke the other sessions of THIS application only.
        if (settings.Sessions.SingleActiveSession)
        {
            await sessions.RevokeAllAsync(user, app, "session.revoked_others", ct);
            // With per-application credentials the stamp belongs to this application. A shared stamp would also log the
            // user out of the other applications, so it is left alone in that case.
            if (await flow.GetCredentialScopeAsync(ct) == CredentialScope.PerApplication)
                await users.UpdateSecurityStampAsync(user);
        }

        await audit.WriteAsync(Entry("login.success", app, user, ip, ua, new() { ["amr"] = amr }), ct);
        return new LoginResult { Succeeded = true, User = user, Application = app, Amr = amr, SessionId = Guid.NewGuid().ToString("N") };
    }

    /// <summary>Writes the audit event and returns the failure. <paramref name="publicCode"/> is what the caller sees; the audit keeps the real reason.</summary>
    private async Task<LoginResult> FailAsync(Auth2Application app, AppUser? user, string reasonCode, PasswordLoginRequest? request, CancellationToken ct,
        Dictionary<string, object?>? data = null, string? publicCode = null, string? ipAddress = null)
    {
        data ??= [];
        data["code"] = reasonCode;   // never the login name or the password
        await audit.WriteAsync(Entry(FailedAction, app, user, request?.IpAddress ?? ipAddress, request?.UserAgent, data), ct);
        return LoginResult.Fail(publicCode ?? reasonCode);
    }

    private AuditEntry Entry(string action, Auth2Application app, AppUser? user, string? ip, string? ua, Dictionary<string, object?>? data = null) =>
        new(action, app.Id, ActorUserId: user?.Id, TargetType: user is null ? null : "user", TargetId: user?.Id.ToString(), IpAddress: ip, UserAgent: ua, Data: data);

    private static LoginMethod Classify(string login, ApplicationSettings settings)
    {
        if (login.Contains('@')) return LoginMethod.EmailPassword;
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(settings.Phone.RegexTimeoutMs, 1, 1000));
        return Auth2UserValidator.SafeMatch(settings.Phone.PhoneNumberRegex, login, timeout) ? LoginMethod.PhonePassword : LoginMethod.UserNamePassword;
    }

    private async Task<AppUser?> FindUserAsync(string login, LoginMethod method, ApplicationSettings settings, CancellationToken ct)
    {
        login = login.Trim();
        switch (method)
        {
            case LoginMethod.EmailPassword:
                return await users.FindByEmailAsync(login);
            case LoginMethod.PhonePassword:
                var matches = await db.Users.Where(u => u.PhoneNumber == login).Take(2).ToListAsync(ct);
                return matches.Count == 1 ? matches[0] : null;   // two users with one number: refuse (treated as unknown)
            default:
                return await users.FindByNameAsync(login);
        }
    }

    private async Task<bool> IsMemberAsync(AppUser user, Auth2Application app, CancellationToken ct)
    {
        if (options.Value.Mode == ServerMode.SingleApplication) return true;   // user_applications is not used in single mode
        return await db.UserApplications.AnyAsync(x => x.UserId == user.Id && x.ApplicationId == app.Id && x.Status == UserApplicationStatus.Active, ct);
    }

    private string CreateTwoFactorToken(AppUser user, Auth2Application app) =>
        TokenProtector.Protect($"{user.Id:N}|{app.Id:N}", TwoFactorTokenLifetime);

    private async Task<AppUser?> ReadTwoFactorTokenAsync(string token, Auth2Application app)
    {
        try
        {
            var parts = TokenProtector.Unprotect(token).Split('|');
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var userId) || !Guid.TryParseExact(parts[1], "N", out var appId) || appId != app.Id)
                return null;
            return await users.FindByIdAsync(userId.ToString());
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;   // expired, changed or made for something else
        }
    }
}
