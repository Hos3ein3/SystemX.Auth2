namespace SystemX.Auth2.Contracts.Constants;

/// <summary>Stable error codes. Also used as localization keys.</summary>
public static class Auth2ErrorCodes
{
    /// <summary>Wrong user or wrong password (same code on purpose).</summary>
    public const string InvalidCredentials = "invalid_credentials";
    /// <summary>The account is locked.</summary>
    public const string LockedOut = "locked_out";
    /// <summary>A second factor is needed.</summary>
    public const string TwoFactorRequired = "two_factor_required";
    /// <summary>The user does not belong to this application.</summary>
    public const string NotRegisteredInApplication = "not_registered_in_application";
    /// <summary>The application does not exist.</summary>
    public const string ApplicationNotFound = "application_not_found";
    /// <summary>The application is disabled.</summary>
    public const string ApplicationDisabled = "application_disabled";
    /// <summary>ProjectKey or application name does not match.</summary>
    public const string ProjectKeyOrNameMismatch = "project_key_or_name_mismatch";
    /// <summary>Client and server use different modes.</summary>
    public const string ServerModeMismatch = "server_mode_mismatch";
    /// <summary>Password is too short.</summary>
    public const string PasswordTooShort = "password_too_short";
    /// <summary>Password needs a digit.</summary>
    public const string PasswordRequiresDigit = "password_requires_digit";
    /// <summary>Password needs a lowercase letter.</summary>
    public const string PasswordRequiresLower = "password_requires_lower";
    /// <summary>Password needs an uppercase letter.</summary>
    public const string PasswordRequiresUpper = "password_requires_upper";
    /// <summary>Password needs a non-alphanumeric character.</summary>
    public const string PasswordRequiresNonAlphanumeric = "password_requires_non_alphanumeric";
    /// <summary>Password is too long.</summary>
    public const string PasswordTooLong = "password_too_long";
    /// <summary>Password needs more different characters.</summary>
    public const string PasswordRequiresUniqueChars = "password_requires_unique_chars";
    /// <summary>Password is on the banned list.</summary>
    public const string PasswordBanned = "password_banned";
    /// <summary>User name has a wrong length or wrong characters.</summary>
    public const string InvalidUserName = "invalid_user_name";
    /// <summary>Email is not valid.</summary>
    public const string InvalidEmail = "invalid_email";
    /// <summary>The email domain is not allowed in this application.</summary>
    public const string EmailDomainNotAllowed = "email_domain_not_allowed";
    /// <summary>Phone number is not valid.</summary>
    public const string InvalidPhoneNumber = "invalid_phone_number";
    /// <summary>The user does not belong to this application (no row yet).</summary>
    public const string UserNotInApplication = "user_not_in_application";
    /// <summary>Email must be confirmed before login.</summary>
    public const string EmailNotConfirmed = "email_not_confirmed";
    /// <summary>Phone number must be confirmed before login.</summary>
    public const string PhoneNotConfirmed = "phone_not_confirmed";
    /// <summary>Two-factor is required by the application but the user has not set it up yet.</summary>
    public const string TwoFactorSetupRequired = "two_factor_setup_required";
    /// <summary>The grant type is not allowed for this application or client.</summary>
    public const string GrantNotAllowed = "grant_not_allowed";
    /// <summary>Two-factor is not on for this user.</summary>
    public const string TwoFactorNotEnabled = "two_factor_not_enabled";
    /// <summary>The application does not allow authenticator-app two-factor.</summary>
    public const string TwoFactorNotAllowed = "two_factor_not_allowed";
    /// <summary>Consent is needed (OIDC <c>prompt=none</c> cannot ask).</summary>
    public const string ConsentRequired = "consent_required";
    /// <summary>Login method not allowed.</summary>
    public const string LoginMethodNotAllowed = "login_method_not_allowed";
    /// <summary>Registration is closed.</summary>
    public const string RegistrationDisabled = "registration_disabled";
    /// <summary>Token is missing or invalid.</summary>
    public const string InvalidToken = "invalid_token";
    /// <summary>Scope is missing.</summary>
    public const string InsufficientScope = "insufficient_scope";
    /// <summary>The caller may not do this.</summary>
    public const string Forbidden = "forbidden";
    /// <summary>Validation failed.</summary>
    public const string ValidationFailed = "validation_failed";
    /// <summary>The record was not found.</summary>
    public const string NotFound = "not_found";
    /// <summary>The record was changed by someone else.</summary>
    public const string Conflict = "conflict";
    /// <summary>Too many requests.</summary>
    public const string RateLimited = "rate_limited";
    /// <summary>OTP code is wrong or expired.</summary>
    public const string InvalidOtp = "invalid_otp";
    /// <summary>Proof of ownership is needed to join an application.</summary>
    public const string OwnershipProofRequired = "ownership_proof_required";
    /// <summary>Sync is not allowed for this application.</summary>
    public const string SyncNotAllowed = "sync_not_allowed";
    /// <summary>A permission key is invalid.</summary>
    public const string InvalidPermissionKey = "invalid_permission_key";
    /// <summary>A scope name is invalid, or it is reserved (<c>auth2.*</c> and the standard OpenID Connect scopes).</summary>
    public const string InvalidScopeName = "invalid_scope_name";
    /// <summary>The provider gave an email that is not verified, and a user with that email exists: nothing was linked.</summary>
    public const string ExternalEmailExistsNotVerified = "external_email_exists_not_verified";
    /// <summary>Registration through this external provider is not allowed.</summary>
    public const string ExternalRegistrationNotAllowed = "external_registration_not_allowed";
    /// <summary>The external provider is disabled or unknown.</summary>
    public const string ExternalProviderDisabled = "external_provider_disabled";
    /// <summary>The provider's answer is missing something or is not valid.</summary>
    public const string ExternalClaimsInvalid = "external_claims_invalid";
    /// <summary>The user cannot remove the last way to sign in.</summary>
    public const string CannotRemoveLastLoginMethod = "cannot_remove_last_login_method";
    /// <summary>A SAML response did not pass validation.</summary>
    public const string SamlResponseInvalid = "saml_response_invalid";
    /// <summary>A SAML response, assertion or request id was used before.</summary>
    public const string SamlReplayDetected = "saml_replay_detected";
    /// <summary>The directory could not be reached.</summary>
    public const string LdapUnavailable = "ldap_unavailable";
    /// <summary>The directory account is disabled (only shown when the application reveals it).</summary>
    public const string LdapAccountDisabled = "ldap_account_disabled";
    /// <summary>Passkeys are not allowed here.</summary>
    public const string PasskeyNotAllowed = "passkey_not_allowed";
    /// <summary>The user has the maximum number of passkeys.</summary>
    public const string PasskeyLimitReached = "passkey_limit_reached";
    /// <summary>This authenticator model is blocked.</summary>
    public const string PasskeyBlockedAuthenticator = "passkey_blocked_authenticator";
    /// <summary>Too many items in one request.</summary>
    public const string TooManyItems = "too_many_items";
    /// <summary>Redirect URI not allowed.</summary>
    public const string InvalidRedirectUri = "invalid_redirect_uri";
    /// <summary>Unexpected server error.</summary>
    public const string ServerError = "server_error";

    /// <summary>All codes. Each one needs an English text in <c>Errors.resx</c>.</summary>
    public static IReadOnlyList<string> All { get; } = typeof(Auth2ErrorCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}
