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
    /// <summary>Change the own user name (user token).</summary>
    public const string UserNameChange = Base + "/user-name/change";
    /// <summary>Start an email change: a link goes to the new address (user token).</summary>
    public const string EmailChange = Base + "/email/change";
    /// <summary>Finish an email change with the token of the link (user token).</summary>
    public const string EmailChangeConfirm = Base + "/email/change/confirm";
    /// <summary>Start a phone change: a code goes to the new number (user token).</summary>
    public const string PhoneChange = Base + "/phone/change";
    /// <summary>Finish a phone change with the code (user token).</summary>
    public const string PhoneChangeConfirm = Base + "/phone/change/confirm";
    /// <summary>Finish joining a second application (proof of ownership + new password).</summary>
    public const string JoinComplete = Base + "/join/complete";
    /// <summary>Start TOTP setup.</summary>
    public const string TwoFactorSetup = Base + "/2fa/setup";
    /// <summary>Enable TOTP.</summary>
    public const string TwoFactorEnable = Base + "/2fa/enable";
    /// <summary>Disable TOTP.</summary>
    public const string TwoFactorDisable = Base + "/2fa/disable";
    /// <summary>Checks one code of the authenticator app for the signed-in user (a fresh proof before a sensitive action).</summary>
    public const string TwoFactorVerify = Base + "/2fa/verify";
    /// <summary>Recovery codes.</summary>
    public const string TwoFactorRecoveryCodes = Base + "/2fa/recovery-codes";
    /// <summary>Current user.</summary>
    public const string Me = Base + "/me";
    /// <summary>Client handshake.</summary>
    public const string Handshake = Base + "/client/handshake";
    /// <summary>The default path where a client application answers the Server's health question (two-way health check). It is a route of the <b>client</b>, not of the Server.</summary>
    public const string ClientHealth = "/auth2/health";
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
    /// <summary>Lists the enabled external sign-in providers of the application (client-credentials token with the handshake scope).</summary>
    public const string ExternalProviders = Base + "/external-providers";
    /// <summary>Scope sync (POST).</summary>
    public const string ScopesSync = Base + "/scopes/sync";
    /// <summary>Access of one user. Replace <c>{id}</c>.</summary>
    public const string UserAccess = Base + "/users/{id}/access";
    /// <summary>Base path of the pages and JSON calls of a signed-in person (passkeys).</summary>
    public const string AccountBase = "/account";
    /// <summary>Passkey: options to create one (signed in).</summary>
    public const string PasskeyCreationOptions = AccountBase + "/passkey/creation-options";
    /// <summary>Passkey: saves the new one (signed in).</summary>
    public const string PasskeyRegister = AccountBase + "/passkey/register";
    /// <summary>Passkey: the list of the signed-in person.</summary>
    public const string Passkeys = AccountBase + "/passkeys";
    /// <summary>Passkey: one of the signed-in person (rename, delete).</summary>
    public const string PasskeyById = AccountBase + "/passkeys/{id}";
    /// <summary>Passkey: options to sign in.</summary>
    public const string PasskeyRequestOptions = AccountBase + "/passkey/request-options";
    /// <summary>Passkey: signs in.</summary>
    public const string PasskeySignIn = AccountBase + "/passkey/signin";
    /// <summary>The list of external sign-in providers of the application (hosted UI).</summary>
    public const string ExternalProviderList = "/connect/external/providers";
    /// <summary>Starts an external sign-in.</summary>
    public const string ExternalChallenge = "/connect/external/challenge";
    /// <summary>A branding asset by project id and kind (public).</summary>
    public const string BrandingAssetById = "/branding/assets/{projectId:guid}/{kind}";
    /// <summary>A branding asset by project key and kind (public).</summary>
    public const string BrandingAssetByKey = "/branding/assets/{projectKey}/{kind}";
    /// <summary>A branding asset of a project by key (short form).</summary>
    public const string ProjectBrandingAsset = "/projects/{projectKey}/{kind}";
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
