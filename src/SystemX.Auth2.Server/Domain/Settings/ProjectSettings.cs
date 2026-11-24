using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Server.Domain.Settings;

/// <summary>Who may edit a setting.</summary>
public enum SettingEditor
{
    /// <summary>Only a global admin (the developer).</summary>
    GlobalOnly = 0,
    /// <summary>A project admin may edit it too.</summary>
    ProjectAdminEditable = 1,
}

/// <summary>Marks who may edit a setting. Every setting field must have it (a test checks this).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingEditabilityAttribute(SettingEditor editor) : Attribute
{
    /// <summary>The editor level.</summary>
    public SettingEditor Editor { get; } = editor;
}

/// <summary>Join mode of a project.</summary>
public enum JoinMode
{
    /// <summary>A user with proof of ownership may join.</summary>
    AutoJoin = 0,
    /// <summary>Only invited users may join.</summary>
    InviteOnly = 1,
    /// <summary>Nobody may join.</summary>
    Closed = 2,
}

/// <summary>Two-factor mode.</summary>
public enum TwoFactorMode
{
    /// <summary>No two-factor.</summary>
    Off = 0,
    /// <summary>User may turn it on.</summary>
    Optional = 1,
    /// <summary>Everybody needs it.</summary>
    Required = 2,
}

/// <summary>
/// All settings of one project (the project level). Applications override a few of them, see <see cref="ClientApplicationSettings"/>. Stored as jsonb. Every field has a safe default.
/// See <c>docs/server-settings.md</c>.
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>Current JSON schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the stored JSON.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Login and registration methods.</summary>
    public IdentityMethodSettings Methods { get; set; } = new();
    /// <summary>User name rules.</summary>
    public UserNameSettings UserName { get; set; } = new();
    /// <summary>Password rules.</summary>
    public PasswordSettings Password { get; set; } = new();
    /// <summary>Email rules.</summary>
    public EmailSettings Email { get; set; } = new();
    /// <summary>Phone rules.</summary>
    public PhoneSettings Phone { get; set; } = new();
    /// <summary>Registration rules.</summary>
    public RegistrationSettings Registration { get; set; } = new();
    /// <summary>Lockout rules.</summary>
    public LockoutSettings Lockout { get; set; } = new();
    /// <summary>Two-factor and OTP rules.</summary>
    public TwoFactorSettings TwoFactor { get; set; } = new();
    /// <summary>Sessions and tokens.</summary>
    public SessionSettings Sessions { get; set; } = new();
    /// <summary>Redirect rules.</summary>
    public RedirectSettings Redirects { get; set; } = new();
    /// <summary>Client behaviour.</summary>
    public ClientSettings Clients { get; set; } = new();
    /// <summary>Localization and branding.</summary>
    public LocalizationSettings Localization { get; set; } = new();
    /// <summary>
    /// External identity providers (social login, OIDC, SAML, LDAP). Changed only through the provider admin routes (they check each field):
    /// the generic settings route ignores this list.
    /// </summary>
    public List<ExternalProviderSettings> ExternalProviders { get; set; } = [];
    /// <summary>How emails and SMS are sent for this project. Changed only through the messaging admin routes (they hold a secret).</summary>
    public MessagingSettings Messaging { get; set; } = new();
    /// <summary>Passkey rules.</summary>
    public PasskeySettings Passkeys { get; set; } = new();

    /// <summary>Per-project override of the server credential scope. Only used if the server allows it.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public CredentialScope? CredentialScope { get; set; }
}

/// <summary>Login and registration methods.</summary>
public sealed class IdentityMethodSettings
{
    /// <summary>Allowed login methods.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)]
    public List<LoginMethod> AllowedLoginMethods { get; set; } = [LoginMethod.UserNamePassword, LoginMethod.EmailPassword];
    /// <summary>Allowed register methods.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)]
    public List<LoginMethod> AllowedRegisterMethods { get; set; } = [LoginMethod.UserNamePassword, LoginMethod.EmailPassword];
}

/// <summary>User name rules.</summary>
public sealed class UserNameSettings
{
    /// <summary>Minimum length.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public int MinLength { get; set; } = 3;
    /// <summary>Maximum length.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public int MaxLength { get; set; } = 64;
    /// <summary>Regex for allowed names. Always matched with a timeout.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string AllowedCharacters { get; set; } = "^[a-zA-Z0-9._@+-]+$";
    /// <summary>User names must be unique.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireUnique { get; set; } = true;
}

/// <summary>Password rules.</summary>
public sealed class PasswordSettings
{
    /// <summary>Minimum length.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int MinLength { get; set; } = 10;
    /// <summary>Maximum length.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int MaxLength { get; set; } = 128;
    /// <summary>Needs a digit.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireDigit { get; set; } = true;
    /// <summary>Needs a lowercase letter.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireLower { get; set; } = true;
    /// <summary>Needs an uppercase letter.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireUpper { get; set; } = true;
    /// <summary>Needs a non-alphanumeric character.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireNonAlphanumeric { get; set; } = false;
    /// <summary>Minimum number of different characters.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int UniqueChars { get; set; } = 4;
    /// <summary>Passwords that are never allowed. A breached-list check can be plugged in later (hook only).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> BannedPasswords { get; set; } = [];
    /// <summary>
    /// Remember the hashes of the passwords a person had before (when the password changes by change, reset or join). With this on, the new password may not be
    /// the current one. Off by default; nothing is saved and nothing is checked.
    /// </summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool SaveOldPasswords { get; set; }
    /// <summary>How many old passwords are remembered (1 to 24), not counting the current one. The oldest are forgotten. Used with <see cref="SaveOldPasswords"/>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int OldPasswordsToKeep { get; set; } = 5;
    /// <summary>
    /// With <see cref="SaveOldPasswords"/> on: <c>false</c> refuses the current password and every remembered one. <c>true</c> allows an older password again
    /// and refuses only the current one and the one used right before it.
    /// </summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowOldPasswordReuse { get; set; }
    /// <summary>
    /// A password must be changed after this many days (0 = never). When it is older, the password sign-in answers <c>password_expired</c> until the person
    /// chooses a new password (the forgot-password flow). A password without a date counts from the day the person joined.
    /// </summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int PasswordExpiryDays { get; set; }
}

/// <summary>Email rules.</summary>
public sealed class EmailSettings
{
    /// <summary>Email must be confirmed before login.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireConfirmedEmail { get; set; }
    /// <summary>If not empty, only these domains are allowed.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public List<string> AllowedDomains { get; set; } = [];
    /// <summary>These domains are never allowed.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public List<string> BlockedDomains { get; set; } = [];
}

/// <summary>Phone rules.</summary>
public sealed class PhoneSettings
{
    /// <summary>Regex for phone numbers. Default is E.164.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string PhoneNumberRegex { get; set; } = @"^\+[1-9]\d{6,14}$";
    /// <summary>Phone must be confirmed before login.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireConfirmedPhone { get; set; }
    /// <summary>Regex timeout in ms. Protects from ReDoS.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int RegexTimeoutMs { get; set; } = 200;
}

/// <summary>Registration rules.</summary>
public sealed class RegistrationSettings
{
    /// <summary>Is registration open.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public bool RegistrationEnabled { get; set; } = true;
    /// <summary>Join mode.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public JoinMode JoinMode { get; set; } = JoinMode.AutoJoin;
    /// <summary>User must sign in again after registering.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public bool LoginRequiredAfterRegister { get; set; } = true;
    /// <summary>
    /// Send the confirmation email (the link) right after registration. An application can override it. It is always sent when
    /// <c>Email.RequireConfirmedEmail</c> is on, because the user could not sign in otherwise. The user can ask for it again later.
    /// </summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public bool SendConfirmationEmail { get; set; } = true;
    /// <summary>
    /// Role names every new member of the project gets (by self registration, joining with proof, or the first external sign-in).
    /// The roles must exist in the project. A missing one is skipped and written to the audit log. The built-in admin roles are refused.
    /// </summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> AssignedRoles { get; set; } = [];
    /// <summary>Group names every new member gets (keys: lowercase letters, digits and <c>. _ : -</c>). The groups must exist. A missing one is skipped and audited.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> AssignedGroups { get; set; } = [];
    /// <summary>Permission keys every new member gets directly (for example <c>orders.read</c>). The permissions must exist. A missing one is skipped and audited.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> AssignedPermissions { get; set; } = [];
}

/// <summary>Lockout rules.</summary>
public sealed class LockoutSettings
{
    /// <summary>Is lockout on.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool LockoutEnabled { get; set; } = true;
    /// <summary>Failed attempts before lockout.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int MaxFailedAttempts { get; set; } = 5;
    /// <summary>Lockout time in minutes.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int LockoutMinutes { get; set; } = 15;
    /// <summary>Lockout also applies to new users.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool LockoutForNewUsers { get; set; } = true;
}

/// <summary>Two-factor and OTP rules.</summary>
public sealed class TwoFactorSettings
{
    /// <summary>Two-factor mode.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public TwoFactorMode Mode { get; set; } = TwoFactorMode.Optional;
    /// <summary>Allow TOTP apps.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowTotp { get; set; } = true;
    /// <summary>Allow recovery codes.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowRecoveryCodes { get; set; } = true;
    /// <summary>Number of recovery codes.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int RecoveryCodeCount { get; set; } = 10;
    /// <summary>OTP length.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int OtpLength { get; set; } = 6;
    /// <summary>OTP lifetime in seconds.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int OtpLifetimeSeconds { get; set; } = 120;
    /// <summary>Max wrong OTP attempts.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int OtpMaxAttempts { get; set; } = 5;
    /// <summary>Seconds before a new OTP may be sent.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int OtpResendSeconds { get; set; } = 60;
    /// <summary>Allow passkeys (experimental).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowPasskeys { get; set; }
}

/// <summary>Sessions and tokens.</summary>
public sealed class SessionSettings
{
    /// <summary>Access token lifetime in minutes.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int AccessTokenMinutes { get; set; } = 15;
    /// <summary>Refresh token lifetime in days.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int RefreshTokenDays { get; set; } = 14;
    /// <summary>Identity token lifetime in minutes.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int IdentityTokenMinutes { get; set; } = 10;
    /// <summary>Cookie lifetime in minutes. Null means the client default.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int? CookieLifetimeMinutes { get; set; }
    /// <summary>Sliding cookie expiry.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool CookieSliding { get; set; } = true;
    /// <summary>On sign-in, revoke other sessions of this application.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool SingleActiveSession { get; set; }
    /// <summary>Show a consent screen.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireConsent { get; set; }
    /// <summary>How consent works.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public ConsentMode ConsentMode { get; set; } = ConsentMode.Implicit;
}

/// <summary>Redirect fallbacks. A valid return URL wins. The allowed redirect URIs are OpenIddict fields of each application.</summary>
public sealed class RedirectSettings
{
    /// <summary>Where to go after login.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public string? RedirectAfterLogin { get; set; }
    /// <summary>Where to go after register.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public string? RedirectAfterRegister { get; set; }
    /// <summary>Where to go after logout.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public string? RedirectAfterLogout { get; set; }
}

/// <summary>Client behaviour (project defaults for all applications).</summary>
public sealed class ClientSettings
{
    /// <summary>Allowed client types.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)]
    public List<ClientType> AllowedClientTypes { get; set; } = [ClientType.Api, ClientType.Mvc, ClientType.RazorPages, ClientType.Blazor, ClientType.Machine];
    /// <summary>Permission sync is allowed.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool PermissionSyncAllowed { get; set; } = true;
    /// <summary>Custom scope sync is allowed. A client can then register its own scopes (not <c>auth2.*</c>, not the standard ones) and allow them for itself.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool ScopeSyncAllowed { get; set; } = true;
    /// <summary>Group sync (push) is allowed. Off by default: admins normally decide groups. Push only adds missing groups.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool GroupSyncAllowed { get; set; }
    /// <summary>Role sync is allowed.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RoleSyncAllowed { get; set; } = true;
    /// <summary>Sync may remove permissions.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowRemovePermissionsFromSync { get; set; }
    /// <summary>A project admin may rotate the client secret of the own application.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowProjectAdminRotateSecret { get; set; }
    /// <summary>Default UI mode.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public UiMode DefaultUiMode { get; set; } = UiMode.Hosted;
}

/// <summary>Localization and branding.</summary>
public sealed class LocalizationSettings
{
    /// <summary>Default culture.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public string DefaultCulture { get; set; } = "en";
    /// <summary>Supported cultures. They only choose a language.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public List<string> SupportedCultures { get; set; } = ["en"];
    /// <summary>Brand name.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public string? BrandName { get; set; }
    /// <summary>Support email.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public string? SupportEmail { get; set; }
}
