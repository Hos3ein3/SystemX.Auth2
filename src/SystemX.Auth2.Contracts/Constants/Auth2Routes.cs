namespace SystemX.Auth2.Contracts.Constants;

/// <summary>Every route string. Client and Server both use this class so they never disagree.</summary>
public static class Auth2Routes
{
    /// <summary>Base path of the JSON API.</summary>
    public const string Base = "/api/auth2";

    /// <summary>Register.</summary>
    public const string Register = Base + "/register";
    /// <summary>Request OTP.</summary>
    public const string OtpRequest = Base + "/otp/request";
    /// <summary>Forgot password.</summary>
    public const string PasswordForgot = Base + "/password/forgot";
    /// <summary>Reset password.</summary>
    public const string PasswordReset = Base + "/password/reset";
    /// <summary>Change password.</summary>
    public const string PasswordChange = Base + "/password/change";
    /// <summary>Confirm email.</summary>
    public const string EmailConfirm = Base + "/email/confirm";
    /// <summary>Confirm phone.</summary>
    public const string PhoneConfirm = Base + "/phone/confirm";
    /// <summary>Send the email confirmation again.</summary>
    public const string EmailConfirmResend = Base + "/email/confirm/resend";
    /// <summary>Finish joining a second application (proof of ownership + new password).</summary>
    public const string JoinComplete = Base + "/join/complete";
    /// <summary>Start TOTP setup.</summary>
    public const string TwoFactorSetup = Base + "/2fa/setup";
    /// <summary>Enable TOTP.</summary>
    public const string TwoFactorEnable = Base + "/2fa/enable";
    /// <summary>Disable TOTP.</summary>
    public const string TwoFactorDisable = Base + "/2fa/disable";
    /// <summary>Recovery codes.</summary>
    public const string TwoFactorRecoveryCodes = Base + "/2fa/recovery-codes";
    /// <summary>Current user.</summary>
    public const string Me = Base + "/me";
    /// <summary>Client handshake.</summary>
    public const string Handshake = Base + "/client/handshake";
    /// <summary>Permissions (POST sync, GET pull).</summary>
    public const string Permissions = Base + "/permissions";
    /// <summary>Permission sync.</summary>
    public const string PermissionsSync = Base + "/permissions/sync";
    /// <summary>Roles (GET pull).</summary>
    public const string Roles = Base + "/roles";
    /// <summary>Role sync.</summary>
    public const string RolesSync = Base + "/roles/sync";
    /// <summary>Groups (GET pull).</summary>
    public const string Groups = Base + "/groups";
    /// <summary>Group sync (POST).</summary>
    public const string GroupsSync = Base + "/groups/sync";
    /// <summary>Custom scopes of the application (GET pull).</summary>
    public const string Scopes = Base + "/scopes";
    /// <summary>Scope sync (POST).</summary>
    public const string ScopesSync = Base + "/scopes/sync";
    /// <summary>Access of one user. Replace <c>{id}</c>.</summary>
    public const string UserAccess = Base + "/users/{id}/access";
    /// <summary>Base path of the admin API.</summary>
    public const string Admin = Base + "/admin";

    /// <summary>Liveness check.</summary>
    public const string HealthLive = "/health/live";
    /// <summary>Readiness check.</summary>
    public const string HealthReady = "/health/ready";

    /// <summary>OpenIddict authorize endpoint.</summary>
    public const string Authorize = "/connect/authorize";
    /// <summary>OpenIddict token endpoint.</summary>
    public const string Token = "/connect/token";
    /// <summary>OpenIddict end-session endpoint.</summary>
    public const string EndSession = "/connect/endsession";
    /// <summary>OpenIddict userinfo endpoint.</summary>
    public const string UserInfo = "/connect/userinfo";
    /// <summary>OpenIddict revocation endpoint.</summary>
    public const string Revoke = "/connect/revoke";
    /// <summary>OpenIddict introspection endpoint.</summary>
    public const string Introspect = "/connect/introspect";

    /// <summary>Builds the user access route for a user id.</summary>
    public static string UserAccessFor(Guid userId) => UserAccess.Replace("{id}", userId.ToString("D"), StringComparison.Ordinal);
}
