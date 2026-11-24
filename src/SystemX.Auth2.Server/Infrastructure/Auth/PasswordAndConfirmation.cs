using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

internal sealed class PasswordService(
    UserManager<AppUser> users, IAuthFlowResolver flow, UserLookup lookup, IUserNotifier notifier, IOtpService otp,
    ISessionService sessions, IAccessVersionService versions, IAuditService audit) : IPasswordService
{
    public async Task ForgotAsync(string login, string? returnUrl = null, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        var user = await lookup.FindAsync(login, settings, ct);

        // Only members get a reset. A person who is not in this application goes through the join flow (with proof).
        if (user is null || await flow.CheckGateAsync(ct) is not null || !user.IsActive || !await lookup.IsMemberAsync(user, app, ct)) return;

        if (!string.IsNullOrEmpty(user.Email))
            await notifier.SendPasswordResetAsync(user, await users.GeneratePasswordResetTokenAsync(user), returnUrl, ct);
        else if (user.PhoneNumberConfirmed && !string.IsNullOrEmpty(user.PhoneNumber))
        {
            var issued = await otp.IssueAsync(user, Auth2OtpPurposes.PasswordReset, ct);
            if (issued.Code is not null) await notifier.SendOtpAsync(user, Auth2OtpPurposes.PasswordReset, issued.Code, NotificationChannel.Sms, ct);
        }
        else return;

        await audit.WriteAsync(new AuditEntry("password.reset_requested", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
    }

    public async Task<OperationResult> ResetAsync(string login, string token, string newPassword, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        var user = await lookup.FindAsync(login, settings, ct);
        if (user is null || await flow.CheckGateAsync(ct) is not null || !user.IsActive || !await lookup.IsMemberAsync(user, app, ct))
            return OperationResult.Fail(Auth2ErrorCodes.InvalidToken);

        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded && token.All(char.IsDigit) && token.Length == settings.TwoFactor.OtpLength
            && result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
        {
            // The token is a code from an SMS. Check the password rules before the code is used up.
            var problems = new List<IdentityError>();
            foreach (var validator in users.PasswordValidators)
            {
                var r = await validator.ValidateAsync(users, user, newPassword);
                if (!r.Succeeded) problems.AddRange(r.Errors);
            }
            if (problems.Count > 0) return OperationResult.Fail(problems);
            if (await otp.VerifyAsync(user, Auth2OtpPurposes.PasswordReset, token, ct) == OtpVerifyResult.Ok)
                result = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), newPassword);
        }

        if (!result.Succeeded)
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)) ? OperationResult.Fail(Auth2ErrorCodes.InvalidToken) : OperationResult.Fail(result.Errors);

        // The old password is gone: clear the lockout, end every session of this application, and refresh cached access data.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await sessions.RevokeAllAsync(user, app, "session.revoked_password_reset", ct: ct);
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("password.reset", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ChangeAsync(AppUser user, string currentPassword, string newPassword, string? keepAuthorizationId, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                await users.AccessFailedAsync(user);   // guessing the current password counts as a failed attempt
                return OperationResult.Fail(Auth2ErrorCodes.InvalidCredentials);
            }
            return OperationResult.Fail(result.Errors);
        }
        await sessions.RevokeAllAsync(user, app, "session.revoked_password_change", keepAuthorizationId, ct);
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("password.changed", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return OperationResult.Ok;
    }
}

internal sealed class ConfirmationService(
    UserManager<AppUser> users, IAuthFlowResolver flow, UserLookup lookup, IUserNotifier notifier, IOtpService otp, IAuditService audit) : IConfirmationService
{
    public async Task<OperationResult> ConfirmEmailAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return OperationResult.Fail(Auth2ErrorCodes.InvalidToken);
        var result = await users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded) return OperationResult.Fail(Auth2ErrorCodes.InvalidToken);
        await audit.WriteAsync(new AuditEntry("email.confirmed", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task ResendEmailAsync(string login, string? returnUrl = null, CancellationToken ct = default)
    {
        var settings = await flow.GetSettingsAsync(ct);
        var user = await lookup.FindAsync(login, settings, ct);
        if (user is null || !user.IsActive || user.EmailConfirmed || string.IsNullOrEmpty(user.Email)) return;
        await notifier.SendEmailConfirmationAsync(user, await users.GenerateEmailConfirmationTokenAsync(user), returnUrl, ct);
    }

    public async Task<OperationResult> ConfirmPhoneAsync(string login, string code, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        var user = await lookup.FindAsync(login, settings, ct);
        if (user is null || string.IsNullOrEmpty(user.PhoneNumber)) return OperationResult.Fail(Auth2ErrorCodes.InvalidOtp);
        if (await otp.VerifyAsync(user, Auth2OtpPurposes.ConfirmPhone, code, ct) != OtpVerifyResult.Ok) return OperationResult.Fail(Auth2ErrorCodes.InvalidOtp);

        user.PhoneNumberConfirmed = true;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await audit.WriteAsync(new AuditEntry("phone.confirmed", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return OperationResult.Ok;
    }
}

internal sealed class OtpRequestService(UserLookup lookup, IAuthFlowResolver flow, IOtpService otp, IUserNotifier notifier, UserManager<AppUser> users) : IOtpRequestService
{
    public async Task<OperationResult> RequestAsync(string login, string purpose, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        if (await flow.CheckGateAsync(ct) is not null) return OperationResult.Fail(Auth2ErrorCodes.AccessDenied);

        var kind = lookup.KindOf(login, settings);
        AppUser? user;
        NotificationChannel channel;

        switch (purpose)
        {
            case Auth2OtpPurposes.Login:
                var method = kind switch { IdentifierKind.Email => LoginMethod.EmailOtp, IdentifierKind.Phone => LoginMethod.PhoneOtp, _ => (LoginMethod?)null };
                if (method is null || !settings.Methods.AllowedLoginMethods.Contains(method.Value)) return OperationResult.Fail(Auth2ErrorCodes.LoginMethodNotAllowed);
                user = await lookup.FindAsync(login, settings, ct);
                channel = kind == IdentifierKind.Email ? NotificationChannel.Email : NotificationChannel.Sms;
                if (user is null || !user.IsActive || !await lookup.IsMemberAsync(user, app, ct) || await users.IsLockedOutAsync(user)) return OperationResult.Ok;
                break;

            case Auth2OtpPurposes.ConfirmPhone:
                user = await lookup.FindAsync(login, settings, ct);
                channel = NotificationChannel.Sms;
                if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PhoneNumber) || user.PhoneNumberConfirmed) return OperationResult.Ok;
                break;

            default:
                return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        }

        var issued = await otp.IssueAsync(user, purpose, ct);
        if (issued.Code is not null) await notifier.SendOtpAsync(user, purpose, issued.Code, channel, ct);
        return OperationResult.Ok;   // "too soon" looks the same, so nothing about the account leaks
    }
}
