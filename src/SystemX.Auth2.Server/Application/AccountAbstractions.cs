using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>An email. <c>Data</c> holds the raw token or code for tests and custom templates. Never log it.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, IReadOnlyDictionary<string, string>? Data = null);

/// <summary>An SMS. <c>Data</c> holds the raw code for tests. Never log it.</summary>
public sealed record SmsMessage(string To, string Text, IReadOnlyDictionary<string, string>? Data = null);

/// <summary>Sends emails. The development implementation only logs. Replace it in production.</summary>
public interface IEmailSender
{
    /// <summary>Sends one email.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Sends SMS messages. The development implementation only logs. Replace it in production.</summary>
public interface ISmsSender
{
    /// <summary>Sends one SMS.</summary>
    Task SendAsync(SmsMessage message, CancellationToken ct = default);
}

/// <summary>Builds absolute links to the server pages (for emails).</summary>
public interface IServerUrls
{
    /// <summary>Builds <c>{base}{path}?query</c>. The base is <c>Auth2:Issuer</c>, or the current request.</summary>
    string Build(string path, IEnumerable<KeyValuePair<string, string?>> query);
}

/// <summary>Where a code or link is sent.</summary>
public enum NotificationChannel
{
    /// <summary>Email.</summary>
    Email,
    /// <summary>SMS.</summary>
    Sms,
}

/// <summary>Builds the localized messages and sends them.</summary>
public interface IUserNotifier
{
    /// <summary>Sends the email confirmation link.</summary>
    Task SendEmailConfirmationAsync(AppUser user, string token, string? returnUrl, CancellationToken ct = default);

    /// <summary>Sends the password reset link.</summary>
    Task SendPasswordResetAsync(AppUser user, string token, string? returnUrl, CancellationToken ct = default);

    /// <summary>Sends a one-time code by email or SMS.</summary>
    Task SendOtpAsync(AppUser user, string purpose, string code, NotificationChannel channel, CancellationToken ct = default);
}

/// <summary>Result of <see cref="IOtpService.IssueAsync"/>.</summary>
public sealed record OtpIssueResult(string? Code, bool TooSoon)
{
    /// <summary>True when a code was made.</summary>
    public bool Issued => Code is not null;
}

/// <summary>Result of checking a one-time code.</summary>
public enum OtpVerifyResult
{
    /// <summary>The code is right. It is used up now.</summary>
    Ok,
    /// <summary>Wrong code.</summary>
    Invalid,
    /// <summary>The code is too old.</summary>
    Expired,
    /// <summary>Too many wrong attempts. The code is dead.</summary>
    TooManyAttempts,
    /// <summary>There is no open code for this user and purpose.</summary>
    NotFound,
}

/// <summary>
/// One-time codes. Codes are hashed, short-lived, limited in attempts, can be requested again only after a delay,
/// and work once. The database table is used when Redis is off (phase 10).
/// </summary>
public interface IOtpService
{
    /// <summary>Makes a new code for the user, the current application and the purpose. Older open codes are closed.</summary>
    Task<OtpIssueResult> IssueAsync(AppUser user, string purpose, CancellationToken ct = default);

    /// <summary>Checks a code in constant time.</summary>
    Task<OtpVerifyResult> VerifyAsync(AppUser user, string purpose, string code, CancellationToken ct = default);
}

/// <summary>Result of an account operation. Identity errors are kept so the caller can show the password rules.</summary>
public sealed record OperationResult
{
    /// <summary>True when it worked.</summary>
    public bool Succeeded => ErrorCode is null;
    /// <summary>Stable error code, or null.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>All errors (for example every broken password rule).</summary>
    public IReadOnlyList<IdentityError> Errors { get; init; } = [];

    /// <summary>Success.</summary>
    public static OperationResult Ok { get; } = new();

    /// <summary>Failure with a code.</summary>
    public static OperationResult Fail(string code) => new() { ErrorCode = code };

    /// <summary>Failure from Identity errors. The first error gives the code.</summary>
    public static OperationResult Fail(IEnumerable<IdentityError> errors)
    {
        var list = errors.ToList();
        return new OperationResult { ErrorCode = list.FirstOrDefault()?.Code ?? "validation_failed", Errors = list };
    }
}

/// <summary>Result of a registration.</summary>
public sealed record RegisterOutcome
{
    /// <summary>Operation result (errors such as weak password).</summary>
    public OperationResult Result { get; init; } = OperationResult.Ok;
    /// <summary>The user id.</summary>
    public Guid? UserId { get; init; }
    /// <summary>The user was created by this call.</summary>
    public bool UserWasCreated { get; init; }
    /// <summary>The user belongs to the application now.</summary>
    public bool JoinedProject { get; init; }
    /// <summary>Something must be confirmed first (email, phone, or the proof code for joining).</summary>
    public bool ConfirmationRequired { get; init; }
    /// <summary>The new user (only when <see cref="UserWasCreated"/>).</summary>
    public AppUser? User { get; init; }
}

/// <summary>
/// Registration. The application always comes from the client token, never from the body.
/// It is idempotent: it creates the person only when they do not exist, and it never changes the password of an existing person.
/// A person who already exists but is not in this application gets a proof code instead (see <see cref="IJoinService"/>).
/// </summary>
public interface IRegisterService
{
    /// <summary>Registers a user in the current application.</summary>
    Task<RegisterOutcome> RegisterAsync(RegisterRequest request, string? returnUrl = null, CancellationToken ct = default);
}

/// <summary>
/// Joining a second application. Never "just add a row": the person must prove they own the account with a one-time code sent to a
/// confirmed email or phone, and then sets a password for this application.
/// </summary>
public interface IJoinService
{
    /// <summary>Sends the proof code. Silent when the person is unknown, has no confirmed email or phone, or asked too soon.</summary>
    Task RequestAsync(string login, CancellationToken ct = default);

    /// <summary>Sends the proof code to a user that is already known.</summary>
    Task RequestForUserAsync(AppUser user, CancellationToken ct = default);

    /// <summary>Checks the code, creates the membership row and sets the password for this application (same transaction).</summary>
    Task<OperationResult> CompleteAsync(string login, string code, string newPassword, CancellationToken ct = default);
}

/// <summary>Forgot, reset and change password.</summary>
public interface IPasswordService
{
    /// <summary>Starts a reset. Always looks the same to the caller.</summary>
    Task ForgotAsync(string login, string? returnUrl = null, CancellationToken ct = default);

    /// <summary>Finishes a reset with the token from the email or the code from the SMS.</summary>
    Task<OperationResult> ResetAsync(string login, string token, string newPassword, CancellationToken ct = default);

    /// <summary>Changes the own password. Other sessions of this application end; <paramref name="keepAuthorizationId"/> stays.</summary>
    Task<OperationResult> ChangeAsync(AppUser user, string currentPassword, string newPassword, string? keepAuthorizationId, CancellationToken ct = default);
}

/// <summary>Email and phone confirmation.</summary>
public interface IConfirmationService
{
    /// <summary>Confirms an email with the token from the email.</summary>
    Task<OperationResult> ConfirmEmailAsync(Guid userId, string token, CancellationToken ct = default);

    /// <summary>Sends the email confirmation again. Always looks the same to the caller.</summary>
    Task ResendEmailAsync(string login, string? returnUrl = null, CancellationToken ct = default);

    /// <summary>Confirms a phone number with the code from the SMS.</summary>
    Task<OperationResult> ConfirmPhoneAsync(string login, string code, CancellationToken ct = default);
}

/// <summary>Asks for one-time codes (login and phone confirmation).</summary>
public interface IOtpRequestService
{
    /// <summary>Sends a code. Looks the same to the caller whether the user exists or not. Returns an error only for configuration problems.</summary>
    Task<OperationResult> RequestAsync(string login, string purpose, CancellationToken ct = default);
}

/// <summary>Authenticator-app two-factor for a signed-in user.</summary>
public interface ITwoFactorService
{
    /// <summary>Makes a key for the authenticator app.</summary>
    Task<(OperationResult Result, TwoFactorSetupResponse? Setup)> SetupAsync(AppUser user, CancellationToken ct = default);

    /// <summary>Turns two-factor on after the first code. Returns the recovery codes (shown once).</summary>
    Task<(OperationResult Result, IReadOnlyList<string> RecoveryCodes)> EnableAsync(AppUser user, string code, CancellationToken ct = default);

    /// <summary>Turns two-factor off. Needs the password. Refused when the application requires two-factor.</summary>
    Task<OperationResult> DisableAsync(AppUser user, string password, CancellationToken ct = default);

    /// <summary>Makes new recovery codes. Needs a code from the authenticator app.</summary>
    Task<(OperationResult Result, IReadOnlyList<string> Codes)> RegenerateRecoveryCodesAsync(AppUser user, string code, CancellationToken ct = default);
}

/// <summary>Raises the access versions, so caches of access data (the <c>pv</c> claim) are refreshed.</summary>
public interface IAccessVersionService
{
    /// <summary>Raises the user's version in the current application.</summary>
    Task BumpUserAsync(AppUser user, CancellationToken ct = default);

    /// <summary>Raises the application's version.</summary>
    Task BumpProjectAsync(CancellationToken ct = default);
}

/// <summary>Consent rules and records.</summary>
public interface IConsentService
{
    /// <summary>True when the user must be asked. A valid grant token (made by the consent page) satisfies it.</summary>
    Task<bool> IsConsentNeededAsync(AppUser user, ClientApplicationInfo app, EffectiveSettings settings, IReadOnlyCollection<string> scopes, string? grantToken, CancellationToken ct = default);

    /// <summary>Makes a short-lived token that says "this user agreed to these scopes for this application".</summary>
    string CreateGrantToken(AppUser user, ClientApplicationInfo app, IEnumerable<string> scopes);

    /// <summary>Id of a saved (permanent) authorization that covers the scopes, or null.</summary>
    Task<string?> FindAuthorizationIdAsync(AppUser user, ClientApplicationInfo app, IReadOnlyCollection<string> scopes, CancellationToken ct = default);

    /// <summary>Saves a permanent authorization (explicit mode) and returns its id.</summary>
    Task<string> SaveAuthorizationAsync(AppUser user, ClientApplicationInfo app, System.Security.Claims.ClaimsPrincipal principal, IReadOnlyCollection<string> scopes, CancellationToken ct = default);
}

/// <summary>Which consent mode applies.</summary>
public static class ConsentPolicy
{
    /// <summary>With <c>RequireConsent</c> off the mode is implicit. With it on, an implicit mode means explicit.</summary>
    public static ConsentMode Effective(ProjectSettings s) =>
        !s.Sessions.RequireConsent ? ConsentMode.Implicit
        : s.Sessions.ConsentMode == ConsentMode.Implicit ? ConsentMode.Explicit
        : s.Sessions.ConsentMode;
}

/// <summary>One signed-in session of a user in an application (an OpenIddict authorization).</summary>
public sealed record SessionInfo(string Id, DateTimeOffset? CreatedAtUtc, IReadOnlyList<string> Scopes);
