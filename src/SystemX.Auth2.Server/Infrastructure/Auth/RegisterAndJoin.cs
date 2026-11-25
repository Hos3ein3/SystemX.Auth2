using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

internal sealed class RegisterService(
    UserManager<AppUser> users, Auth2DbContext db, IAuthFlowResolver flow, UserLookup lookup, IUserNotifier notifier,
    IOtpService otp, IJoinService join, IAuditService audit, IRegistrationDefaults defaults, TimeProvider clock) : IRegisterService
{
    public async Task<RegisterOutcome> RegisterAsync(RegisterRequest request, string? returnUrl = null, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        if (await flow.CheckGateAsync(ct) is not null) return Fail(Auth2ErrorCodes.AccessDenied);
        if (!settings.Registration.RegistrationEnabled) return Fail(Auth2ErrorCodes.RegistrationDisabled);

        var email = Clean(request.Email);
        var phone = Clean(request.PhoneNumber);
        var userName = Clean(request.UserName);
        if (email is null && phone is null && userName is null) return Fail(Auth2ErrorCodes.ValidationFailed);

        var methods = settings.Methods.AllowedRegisterMethods;
        var emailOk = methods.Contains(LoginMethod.EmailPassword) || methods.Contains(LoginMethod.EmailOtp);
        var phoneOk = methods.Contains(LoginMethod.PhonePassword) || methods.Contains(LoginMethod.PhoneOtp);
        var nameOk = methods.Contains(LoginMethod.UserNamePassword);
        if ((email is not null && !emailOk) || (phone is not null && !phoneOk) || (userName is not null && !nameOk))
            return Fail(Auth2ErrorCodes.LoginMethodNotAllowed);

        var needsPassword = methods.Any(m => m is LoginMethod.UserNamePassword or LoginMethod.EmailPassword or LoginMethod.PhonePassword);
        if (needsPassword && string.IsNullOrEmpty(request.Password)) return Fail(Auth2ErrorCodes.ValidationFailed);

        var name = userName ?? email ?? phone!;

        var existing = await FindExistingAsync(name, email, phone, ct);
        if (existing.Count > 1) return Silent();                 // the identifiers belong to different people: do nothing, say nothing
        if (existing.Count == 1) return await ExistingAsync(existing[0], app, ct);

        var user = new AppUser
        {
            UserName = name, Email = email, PhoneNumber = phone, FirstName = request.FirstName, LastName = request.LastName,
            CreatedAtUtc = clock.GetUtcNow(),
        };

        IdentityResult result;
        try
        {
            result = string.IsNullOrEmpty(request.Password) ? await users.CreateAsync(user) : await users.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request created the same person a moment ago. Treat this call as "the person exists".
            db.ChangeTracker.Clear();
            return await AfterRaceAsync(name, email, phone, app, ct);
        }

        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                db.ChangeTracker.Clear();
                return await AfterRaceAsync(name, email, phone, app, ct);
            }
            return new RegisterOutcome { Result = OperationResult.Fail(result.Errors) };
        }

        // Confirmations (email link, phone code). They are sent even when the application does not force them, unless the setting is off.
        var effective = await flow.GetEffectiveAsync(ct);
        if (email is not null && effective.SendConfirmationEmail) await notifier.SendEmailConfirmationAsync(user, await users.GenerateEmailConfirmationTokenAsync(user), returnUrl, ct);
        if (phone is not null && effective.SendConfirmationSMS)
        {
            var issued = await otp.IssueAsync(user, Auth2OtpPurposes.ConfirmPhone, ct);
            if (issued.Code is not null) await notifier.SendOtpAsync(user, Auth2OtpPurposes.ConfirmPhone, issued.Code, NotificationChannel.Sms, ct);
        }

        await defaults.ApplyAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("register.created", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return new RegisterOutcome
        {
            UserId = user.Id,
            User = user,
            UserWasCreated = true,
            JoinedProject = true,
            ConfirmationRequired = (settings.Email.RequireConfirmedEmail && email is not null) || (settings.Phone.RequireConfirmedPhone && phone is not null),
        };
    }

    private async Task<RegisterOutcome> AfterRaceAsync(string name, string? email, string? phone, Project app, CancellationToken ct)
    {
        var existing = await FindExistingAsync(name, email, phone, ct);
        return existing.Count == 1 ? await ExistingAsync(existing[0], app, ct) : Silent();
    }

    /// <summary>
    /// The person exists. Already a member: nothing changes (idempotent; the password is NOT changed).
    /// Not a member: send a proof code. No row is added and no password is set until the proof is given.
    /// </summary>
    private async Task<RegisterOutcome> ExistingAsync(AppUser user, Project app, CancellationToken ct)
    {
        if (await lookup.HasRowAsync(user, app, ct))
            return new RegisterOutcome { UserId = user.Id, UserWasCreated = false, JoinedProject = true };

        await join.RequestForUserAsync(user, ct);
        return new RegisterOutcome { UserId = user.Id, UserWasCreated = false, JoinedProject = false, ConfirmationRequired = true };
    }

    private async Task<List<AppUser>> FindExistingAsync(string name, string? email, string? phone, CancellationToken ct)
    {
        var normalizedName = users.NormalizeName(name);
        var normalizedEmail = email is null ? null : users.NormalizeEmail(email);
        return await db.Users.Where(u => u.NormalizedUserName == normalizedName
            || (normalizedEmail != null && u.NormalizedEmail == normalizedEmail)
            || (phone != null && u.PhoneNumber == phone)).Take(3).ToListAsync(ct);
    }

    private static RegisterOutcome Fail(string code) => new() { Result = OperationResult.Fail(code) };

    private static RegisterOutcome Silent() => new() { ConfirmationRequired = true };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class JoinService(
    UserManager<AppUser> users, Auth2DbContext db, IAuthFlowResolver flow, UserLookup lookup, IOtpService otp, IUserNotifier notifier,
    IAuditService audit, IRegistrationDefaults defaults, IOptions<Auth2ServerOptions> options, TimeProvider clock) : IJoinService
{
    public async Task RequestAsync(string login, CancellationToken ct = default)
    {
        var settings = await flow.GetSettingsAsync(ct);
        var user = await lookup.FindAsync(login, settings, ct);
        if (user is not null) await RequestForUserAsync(user, ct);
    }

    public async Task RequestForUserAsync(AppUser user, CancellationToken ct = default)
    {
        if (options.Value.Mode == ServerMode.SingleProject) return;
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        if (await flow.CheckGateAsync(ct) is not null || !user.IsActive || settings.Registration.JoinMode != JoinMode.AutoJoin || await lookup.HasRowAsync(user, app, ct)) return;

        // Proof of ownership: a code to an email or phone that was confirmed BEFORE. An unconfirmed address proves nothing.
        var channel = user.EmailConfirmed && !string.IsNullOrEmpty(user.Email) ? NotificationChannel.Email
            : user.PhoneNumberConfirmed && !string.IsNullOrEmpty(user.PhoneNumber) ? NotificationChannel.Sms
            : (NotificationChannel?)null;
        if (channel is null)
        {
            await audit.WriteAsync(new AuditEntry("join.no_proof_channel", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
            return;
        }

        var issued = await otp.IssueAsync(user, Auth2OtpPurposes.Join, ct);
        if (issued.Code is null) return;   // asked again too soon
        await notifier.SendOtpAsync(user, Auth2OtpPurposes.Join, issued.Code, channel.Value, ct);
        await audit.WriteAsync(new AuditEntry("join.requested", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["channel"] = channel.ToString() }), ct);
    }

    public async Task<OperationResult> CompleteAsync(string login, string code, string newPassword, CancellationToken ct = default)
    {
        if (options.Value.Mode == ServerMode.SingleProject) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        var perProject = await flow.GetCredentialScopeAsync(ct) == CredentialScope.PerProject;

        // Every failure about the person or the code looks the same.
        var user = await lookup.FindAsync(login, settings, ct);
        if (user is null || !user.IsActive || await flow.CheckGateAsync(ct) is not null || settings.Registration.JoinMode != JoinMode.AutoJoin || await lookup.HasRowAsync(user, app, ct))
            return OperationResult.Fail(Auth2ErrorCodes.InvalidOtp);

        // Check the new password first, so a weak password does not use up the proof code.
        if (perProject)
        {
            var problems = new List<IdentityError>();
            foreach (var validator in users.PasswordValidators)
            {
                var r = await validator.ValidateAsync(users, user, newPassword);
                if (!r.Succeeded) problems.AddRange(r.Errors);
            }
            if (problems.Count > 0) return OperationResult.Fail(problems);
        }

        if (await otp.VerifyAsync(user, Auth2OtpPurposes.Join, code, ct) != OtpVerifyResult.Ok)
            return OperationResult.Fail(Auth2ErrorCodes.InvalidOtp);

        // The row first, then the password, in one transaction.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.UserProjects.Add(new UserProject
        {
            UserId = user.Id,
            ProjectId = app.Id,
            JoinedAtUtc = clock.GetUtcNow(),
            Status = UserProjectStatus.Active,
            LockoutEnabled = settings.Lockout.LockoutEnabled && settings.Lockout.LockoutForNewUsers,
        });
        await db.SaveChangesAsync(ct);

        if (perProject)
        {
            var set = await users.AddPasswordAsync(user, newPassword);
            if (!set.Succeeded) { await tx.RollbackAsync(ct); return OperationResult.Fail(set.Errors); }
            await users.UpdateSecurityStampAsync(user);
        }
        await tx.CommitAsync(ct);

        await defaults.ApplyAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("application.joined", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return OperationResult.Ok;
    }
}
