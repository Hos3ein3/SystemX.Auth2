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
internal sealed class LoginService(
    UserManager<AppUser> users,
    Auth2DbContext db,
    IAuthFlowResolver flow,
    ISessionService sessions,
    IAuditService audit,
    IOptions<Auth2ServerOptions> options,
    IDataProtectionProvider protection,
    IPasswordHasher<AppUser> hasher,
    UserLookup lookup,
    IOtpService otp,
    IPasswordHistoryService passwordHistory,
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
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);

        // 1. application enabled
        if (await flow.CheckGateAsync(ct) is { } denied) return await FailAsync(app, null, denied, request, ct, publicCode: Auth2ErrorCodes.AccessDenied);

        // 2. which login method is this, and is it allowed?
        var method = MethodOf(lookup.KindOf(request.Login, settings));
        if (!settings.Methods.AllowedLoginMethods.Contains(method))
            return await FailAsync(app, null, Auth2ErrorCodes.LoginMethodNotAllowed, request, ct, new() { ["method"] = method.ToString() });

        var user = await lookup.FindAsync(request.Login, settings, ct);

        // 3. user exists, is active, and belongs to the application (multi mode). Otherwise: the same error as a wrong password.
        if (user is null || !user.IsActive)
        {
            _ = hasher.VerifyHashedPassword(new AppUser(), DummyHash.Value, request.Password);
            return await FailAsync(app, user, Auth2ErrorCodes.InvalidCredentials, request, ct, new() { ["reason"] = user is null ? "unknown_user" : "inactive_user" }, publicCode: Auth2ErrorCodes.InvalidCredentials);
        }
        if (!await lookup.IsMemberAsync(user, app, ct))
        {
            _ = hasher.VerifyHashedPassword(user, DummyHash.Value, request.Password);
            return await FailAsync(app, user, Auth2ErrorCodes.NotRegisteredInProject, request, ct, publicCode: Auth2ErrorCodes.InvalidCredentials);
        }

        // 4. lockout
        if (await users.IsLockedOutAsync(user))
            return await FailAsync(app, user, Auth2ErrorCodes.LockedOut, request, ct);

        // 5. password
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            if (await users.IsLockedOutAsync(user))
            {
                Operations.Auth2Metrics.Lockouts.Add(1);
                await audit.WriteAsync(Entry("login.locked_out", app, user, request.IpAddress, request.UserAgent), ct);
            }
            return await FailAsync(app, user, Auth2ErrorCodes.InvalidCredentials, request, ct, new() { ["reason"] = "wrong_password" });
        }

        // A password that is too old (Password.PasswordExpiryDays) is refused after it was right, so the answer says nothing to a guesser.
        if (await passwordHistory.IsExpiredAsync(user, ct))
            return await FailAsync(app, user, Auth2ErrorCodes.PasswordExpired, request, ct);

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
    public async Task<LoginResult> OtpLoginAsync(OtpLoginRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        if (await flow.CheckGateAsync(ct) is { } denied) return await FailAsync(app, null, denied, null, ct, publicCode: Auth2ErrorCodes.AccessDenied, ipAddress: request.IpAddress);

        // A code can only be sent to an email address or a phone number, never to a user name.
        var kind = lookup.KindOf(request.Login, settings);
        var method = kind switch { IdentifierKind.Email => LoginMethod.EmailOtp, IdentifierKind.Phone => LoginMethod.PhoneOtp, _ => (LoginMethod?)null };
        if (method is null || !settings.Methods.AllowedLoginMethods.Contains(method.Value))
            return await FailAsync(app, null, Auth2ErrorCodes.LoginMethodNotAllowed, null, ct, ipAddress: request.IpAddress);

        var user = await lookup.FindAsync(request.Login, settings, ct);
        if (user is null || !user.IsActive || !await lookup.IsMemberAsync(user, app, ct))
            return await FailAsync(app, user, Auth2ErrorCodes.InvalidOtp, null, ct, new() { ["reason"] = user is null ? "unknown_user" : "not_member_or_inactive" }, ipAddress: request.IpAddress);
        if (await users.IsLockedOutAsync(user))
            return await FailAsync(app, user, Auth2ErrorCodes.LockedOut, null, ct, ipAddress: request.IpAddress);

        var check = await otp.VerifyAsync(user, Auth2OtpPurposes.Login, request.Code.Trim(), ct);
        if (check != OtpVerifyResult.Ok)
        {
            await users.AccessFailedAsync(user);   // guessing codes leads to lockout
            if (await users.IsLockedOutAsync(user)) await audit.WriteAsync(Entry("login.locked_out", app, user, request.IpAddress, request.UserAgent), ct);
            return await FailAsync(app, user, Auth2ErrorCodes.InvalidOtp, null, ct, new() { ["reason"] = check.ToString() }, ipAddress: request.IpAddress);
        }

        // The code went to this address or number, so it is proven now.
        if (kind == IdentifierKind.Email && !user.EmailConfirmed) { user.EmailConfirmed = true; await users.UpdateAsync(user); }
        if (kind == IdentifierKind.Phone && !user.PhoneNumberConfirmed) { user.PhoneNumberConfirmed = true; await users.UpdateAsync(user); }

        return await SucceedAsync(user, app, settings, ["otp"], request.IpAddress, request.UserAgent, ct);
    }

    /// <inheritdoc />
    public async Task<LoginResult> ExternalLoginAsync(ExternalSignInRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        var user = request.User;
        var data = new Dictionary<string, object?> { ["method"] = request.Method.ToString(), ["provider"] = request.ProviderName };

        if (await flow.CheckGateAsync(ct) is { } denied) return await FailAsync(app, user, denied, null, ct, data, Auth2ErrorCodes.AccessDenied, request.IpAddress);
        if (!settings.Methods.AllowedLoginMethods.Contains(request.Method))
            return await FailAsync(app, user, Auth2ErrorCodes.LoginMethodNotAllowed, null, ct, data, ipAddress: request.IpAddress);
        if (!user.IsActive || !await lookup.IsMemberAsync(user, app, ct))
            return await FailAsync(app, user, Auth2ErrorCodes.InvalidCredentials, null, ct, new(data) { ["reason"] = user.IsActive ? "not_member" : "inactive_user" }, ipAddress: request.IpAddress);
        if (await users.IsLockedOutAsync(user))
            return await FailAsync(app, user, Auth2ErrorCodes.LockedOut, null, ct, data, ipAddress: request.IpAddress);
        if (settings.Email.RequireConfirmedEmail && !user.EmailConfirmed)
            return await FailAsync(app, user, Auth2ErrorCodes.EmailNotConfirmed, null, ct, data, ipAddress: request.IpAddress);
        if (settings.Phone.RequireConfirmedPhone && !user.PhoneNumberConfirmed)
            return await FailAsync(app, user, Auth2ErrorCodes.PhoneNotConfirmed, null, ct, data, ipAddress: request.IpAddress);

        if (!request.SkipLocalTwoFactor && settings.TwoFactor.Mode != TwoFactorMode.Off && settings.TwoFactor.AllowTotp)
        {
            if (await users.GetTwoFactorEnabledAsync(user))
                return new LoginResult { ErrorCode = Auth2ErrorCodes.TwoFactorRequired, TwoFactorToken = CreateTwoFactorToken(user, app), Application = app, IdentityProvider = request.ProviderName };
            if (settings.TwoFactor.Mode == TwoFactorMode.Required)
                return await FailAsync(app, user, Auth2ErrorCodes.TwoFactorSetupRequired, null, ct, data, ipAddress: request.IpAddress);
        }
        var result = await SucceedAsync(user, app, settings, [.. request.Amr], request.IpAddress, request.UserAgent, ct);
        return result with { IdentityProvider = request.ProviderName };
    }

    /// <inheritdoc />
    public async Task<LoginResult> CompleteTwoFactorAsync(string twoFactorToken, string? code, string? recoveryCode, string? ipAddress = null, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);

        var user = await ReadTwoFactorTokenAsync(twoFactorToken, app);
        if (user is null) return await FailAsync(app, null, Auth2ErrorCodes.InvalidToken, null, ct, ipAddress: ipAddress);
        if (await flow.CheckGateAsync(ct) is { } denied) return await FailAsync(app, user, denied, null, ct, publicCode: Auth2ErrorCodes.AccessDenied, ipAddress: ipAddress);
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
        var app = await flow.GetProjectAsync(ct);
        return await flow.CheckGateAsync(ct) is null && user.IsActive && await lookup.IsMemberAsync(user, app, ct) && !await users.IsLockedOutAsync(user);
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<LoginResult> SucceedAsync(AppUser user, Project app, ProjectSettings settings, string[] amr, string? ip, string? ua, CancellationToken ct)
    {
        await users.ResetAccessFailedCountAsync(user);

        if (options.Value.Mode == ServerMode.MultiProject)
        {
            var row = await db.UserProjects.FirstOrDefaultAsync(x => x.UserId == user.Id && x.ProjectId == app.Id, ct);
            if (row is not null) { row.LastLoginUtc = clock.GetUtcNow(); await db.SaveChangesAsync(ct); }
        }

        // 7. single active session: revoke the other sessions of THIS application only.
        if (settings.Sessions.SingleActiveSession)
        {
            await sessions.RevokeAllAsync(user, app, "session.revoked_others", ct: ct);
            // With per-application credentials the stamp belongs to this application. A shared stamp would also log the
            // user out of the other applications, so it is left alone in that case.
            if (await flow.GetCredentialScopeAsync(ct) == CredentialScope.PerProject)
                await users.UpdateSecurityStampAsync(user);
        }

        Operations.Auth2Metrics.Logins.Add(1, new KeyValuePair<string, object?>("result", "success"));
        await audit.WriteAsync(Entry("login.success", app, user, ip, ua, new() { ["amr"] = amr }), ct);
        return new LoginResult { Succeeded = true, User = user, Application = app, Amr = amr, SessionId = Guid.NewGuid().ToString("N") };
    }

    /// <summary>Writes the audit event and returns the failure. <paramref name="publicCode"/> is what the caller sees; the audit keeps the real reason.</summary>
    private async Task<LoginResult> FailAsync(Project app, AppUser? user, string reasonCode, PasswordLoginRequest? request, CancellationToken ct,
        Dictionary<string, object?>? data = null, string? publicCode = null, string? ipAddress = null)
    {
        data ??= [];
        data["code"] = reasonCode;   // never the login name or the password
        Operations.Auth2Metrics.Logins.Add(1, new KeyValuePair<string, object?>("result", "failure"));
        await audit.WriteAsync(Entry(FailedAction, app, user, request?.IpAddress ?? ipAddress, request?.UserAgent, data), ct);
        return LoginResult.Fail(publicCode ?? reasonCode);
    }

    private AuditEntry Entry(string action, Project app, AppUser? user, string? ip, string? ua, Dictionary<string, object?>? data = null) =>
        new(action, app.Id, ActorUserId: user?.Id, TargetType: user is null ? null : "user", TargetId: user?.Id.ToString(), IpAddress: ip, UserAgent: ua, Data: data);

    private static LoginMethod MethodOf(IdentifierKind kind) => kind switch
    {
        IdentifierKind.Email => LoginMethod.EmailPassword,
        IdentifierKind.Phone => LoginMethod.PhonePassword,
        _ => LoginMethod.UserNamePassword,
    };

    private string CreateTwoFactorToken(AppUser user, Project app) =>
        TokenProtector.Protect($"{user.Id:N}|{app.Id:N}", TwoFactorTokenLifetime);

    private async Task<AppUser?> ReadTwoFactorTokenAsync(string token, Project app)
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
