using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>See <see cref="IAccountChangeService"/>.</summary>
internal sealed class AccountChangeService(
    UserManager<AppUser> users, IAuthFlowResolver flow, IOtpService otp, IUserNotifier notifier, IAuditService audit) : IAccountChangeService
{
    public async Task<OperationResult> ChangeUserNameAsync(AppUser user, string newUserName, string? currentPassword, CancellationToken ct = default)
    {
        var name = newUserName?.Trim() ?? "";
        if (name.Length == 0 || string.Equals(name, user.UserName, StringComparison.Ordinal)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        if (await CheckPasswordAsync(user, currentPassword) is { } refused) return refused;
        if (await ValidateAsync(user, userName: name) is { } invalid) return invalid;

        var result = await users.SetUserNameAsync(user, name);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await AuditAsync("user.name_changed", user, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> RequestEmailChangeAsync(AppUser user, string newEmail, string? currentPassword, string? returnUrl, CancellationToken ct = default)
    {
        var email = newEmail?.Trim() ?? "";
        if (email.Length == 0 || string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        if (await CheckPasswordAsync(user, currentPassword) is { } refused) return refused;
        if (await ValidateAsync(user, email: email) is { } invalid) return invalid;

        var token = await users.GenerateChangeEmailTokenAsync(user, email);
        await notifier.SendEmailChangeAsync(user, email, token, returnUrl, ct);
        await AuditAsync("email.change_requested", user, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ConfirmEmailChangeAsync(AppUser user, string newEmail, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newEmail) || string.IsNullOrEmpty(token)) return OperationResult.Fail(Auth2ErrorCodes.InvalidToken);
        var result = await users.ChangeEmailAsync(user, newEmail.Trim(), token);
        if (!result.Succeeded)
        {
            // A broken, used or foreign token says nothing more than "invalid"; a rule problem (taken, domain) shows its reason.
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)) ? OperationResult.Fail(Auth2ErrorCodes.InvalidToken) : OperationResult.Fail(result.Errors);
        }
        await AuditAsync("email.changed", user, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> RequestPhoneChangeAsync(AppUser user, string newPhoneNumber, string? currentPassword, CancellationToken ct = default)
    {
        var number = newPhoneNumber?.Trim() ?? "";
        if (number.Length == 0 || (string.Equals(number, user.PhoneNumber, StringComparison.Ordinal) && user.PhoneNumberConfirmed)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        if (await CheckPasswordAsync(user, currentPassword) is { } refused) return refused;
        if (await ValidateAsync(user, phoneNumber: number) is { } invalid) return invalid;

        var issued = await otp.IssueAsync(user, PurposeFor(number), ct);
        if (issued.Code is null) return OperationResult.Fail(Auth2ErrorCodes.RateLimited);   // a code was sent a moment ago
        await notifier.SendPhoneChangeCodeAsync(user, number, issued.Code, ct);
        await AuditAsync("phone.change_requested", user, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ConfirmPhoneChangeAsync(AppUser user, string newPhoneNumber, string code, CancellationToken ct = default)
    {
        var number = newPhoneNumber?.Trim() ?? "";
        if (number.Length == 0 || string.IsNullOrWhiteSpace(code)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        if (await ValidateAsync(user, phoneNumber: number) is { } invalid) return invalid;
        if (await otp.VerifyAsync(user, PurposeFor(number), code.Trim(), ct) != OtpVerifyResult.Ok) return OperationResult.Fail(Auth2ErrorCodes.InvalidOtp);

        user.PhoneNumber = number;
        user.PhoneNumberConfirmed = true;   // the code proved that the person holds this number
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await AuditAsync("phone.changed", user, ct);
        return OperationResult.Ok;
    }

    /// <summary>
    /// The password that confirms a change. Null means it is fine: the person has no password (external account or passkey only) or it is right.
    /// A wrong password counts as a failed attempt, so guessing it locks the account like a sign-in does.
    /// </summary>
    private async Task<OperationResult?> CheckPasswordAsync(AppUser user, string? password)
    {
        if (!await users.HasPasswordAsync(user)) return null;
        if (await users.IsLockedOutAsync(user)) return OperationResult.Fail(Auth2ErrorCodes.LockedOut);
        if (string.IsNullOrEmpty(password)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        if (await users.CheckPasswordAsync(user, password)) return null;
        await users.AccessFailedAsync(user);
        return OperationResult.Fail(Auth2ErrorCodes.InvalidCredentials);
    }

    /// <summary>Runs the user validators (format, project rules, uniqueness) on a copy of the person with the new value. Null means the value is fine.</summary>
    private async Task<OperationResult?> ValidateAsync(AppUser user, string? userName = null, string? email = null, string? phoneNumber = null)
    {
        var probe = new AppUser { Id = user.Id, UserName = userName ?? user.UserName, Email = email ?? user.Email, PhoneNumber = phoneNumber ?? user.PhoneNumber };
        var errors = new List<IdentityError>();
        foreach (var validator in users.UserValidators)
        {
            var result = await validator.ValidateAsync(users, probe);
            if (!result.Succeeded) errors.AddRange(result.Errors);
        }
        return errors.Count == 0 ? null : OperationResult.Fail(errors);
    }

    /// <summary>The purpose of the code is tied to the number, so a code sent for one number cannot confirm another.</summary>
    private static string PurposeFor(string phoneNumber) =>
        Auth2OtpPurposes.ChangePhone + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phoneNumber)))[..16].ToLowerInvariant();

    private async Task AuditAsync(string action, AppUser user, CancellationToken ct) =>
        await audit.WriteAsync(new AuditEntry(action, (await flow.GetProjectAsync(ct)).Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
}
