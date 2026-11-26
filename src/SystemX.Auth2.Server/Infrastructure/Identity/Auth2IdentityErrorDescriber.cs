using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.Identity;

/// <summary>
/// Identity errors with texts from the resx files. Password and user rules use the stable <c>Auth2ErrorCodes</c>;
/// the other errors keep the standard Identity code. The text key is the code (Auth2 codes) or <c>identity.{Code}</c>.
/// </summary>
public class Auth2IdentityErrorDescriber(IAuth2Localizer localizer) : IdentityErrorDescriber
{
    private IdentityError Auth2(string code, params object[] args) => new() { Code = code, Description = localizer.Error(code, args) };
    private IdentityError Std(string code, params object[] args) => new() { Code = code, Description = localizer.Error("identity." + code, args) };

    /// <inheritdoc />
    public override IdentityError DefaultError() => Std(nameof(DefaultError));
    /// <inheritdoc />
    public override IdentityError ConcurrencyFailure() => Std(nameof(ConcurrencyFailure));
    /// <inheritdoc />
    public override IdentityError PasswordMismatch() => Std(nameof(PasswordMismatch));
    /// <inheritdoc />
    public override IdentityError InvalidToken() => Std(nameof(InvalidToken));
    /// <inheritdoc />
    public override IdentityError RecoveryCodeRedemptionFailed() => Std(nameof(RecoveryCodeRedemptionFailed));
    /// <inheritdoc />
    public override IdentityError LoginAlreadyAssociated() => Std(nameof(LoginAlreadyAssociated));
    /// <inheritdoc />
    public override IdentityError InvalidUserName(string? userName) => Auth2(Auth2ErrorCodes.InvalidUserName);
    /// <inheritdoc />
    public override IdentityError InvalidEmail(string? email) => Auth2(Auth2ErrorCodes.InvalidEmail);
    /// <inheritdoc />
    public override IdentityError DuplicateUserName(string userName) => Std(nameof(DuplicateUserName));
    /// <inheritdoc />
    public override IdentityError DuplicateEmail(string email) => Std(nameof(DuplicateEmail));
    /// <inheritdoc />
    public override IdentityError InvalidRoleName(string? role) => Std(nameof(InvalidRoleName));
    /// <inheritdoc />
    public override IdentityError DuplicateRoleName(string role) => Std(nameof(DuplicateRoleName));
    /// <inheritdoc />
    public override IdentityError UserAlreadyHasPassword() => Std(nameof(UserAlreadyHasPassword));
    /// <inheritdoc />
    public override IdentityError UserLockoutNotEnabled() => Std(nameof(UserLockoutNotEnabled));
    /// <inheritdoc />
    public override IdentityError UserAlreadyInRole(string role) => Std(nameof(UserAlreadyInRole));
    /// <inheritdoc />
    public override IdentityError UserNotInRole(string role) => Std(nameof(UserNotInRole));
    /// <inheritdoc />
    public override IdentityError PasswordTooShort(int length) => Auth2(Auth2ErrorCodes.PasswordTooShort, length);
    /// <inheritdoc />
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Auth2(Auth2ErrorCodes.PasswordRequiresUniqueChars, uniqueChars);
    /// <inheritdoc />
    public override IdentityError PasswordRequiresNonAlphanumeric() => Auth2(Auth2ErrorCodes.PasswordRequiresNonAlphanumeric);
    /// <inheritdoc />
    public override IdentityError PasswordRequiresDigit() => Auth2(Auth2ErrorCodes.PasswordRequiresDigit);
    /// <inheritdoc />
    public override IdentityError PasswordRequiresLower() => Auth2(Auth2ErrorCodes.PasswordRequiresLower);
    /// <inheritdoc />
    public override IdentityError PasswordRequiresUpper() => Auth2(Auth2ErrorCodes.PasswordRequiresUpper);

    /// <summary>Password is longer than the maximum.</summary>
    public virtual IdentityError PasswordTooLong(int length) => Auth2(Auth2ErrorCodes.PasswordTooLong, length);
    /// <summary>Password is on the banned list.</summary>
    public virtual IdentityError PasswordBanned() => Auth2(Auth2ErrorCodes.PasswordBanned);
    /// <summary>The password is the current one or a remembered old one.</summary>
    public virtual IdentityError PasswordReused() => Auth2(Auth2ErrorCodes.PasswordReused);
    /// <summary>The email domain is not allowed.</summary>
    public virtual IdentityError EmailDomainNotAllowed(string domain) => Auth2(Auth2ErrorCodes.EmailDomainNotAllowed);
    /// <summary>The phone number is not valid.</summary>
    public virtual IdentityError InvalidPhoneNumber() => Auth2(Auth2ErrorCodes.InvalidPhoneNumber);
    /// <summary>The phone number is the user name of another person.</summary>
    public virtual IdentityError DuplicatePhoneNumber(string phone) => Auth2(Auth2ErrorCodes.PhoneNumberTaken);
}
