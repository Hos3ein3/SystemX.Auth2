using System.Text.Json.Serialization;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Server.Domain.Settings;

/// <summary>Whether the groups of an external source become local roles and groups.</summary>
public enum GroupMappingMode
{
    /// <summary>Groups of the source are ignored.</summary>
    None = 0,
    /// <summary>At every login the mappings are applied (added, and removed only when a mapping mentions them).</summary>
    Sync = 1,
}

/// <summary>When the profile of a user is taken from the source.</summary>
public enum ProfileSyncMode
{
    /// <summary>Names and phone are copied when the user is created. Later logins do not change them.</summary>
    ImportOnce = 0,
    /// <summary>Names and phone are updated at every login.</summary>
    UpdateOnLogin = 1,
}

/// <summary>SAML request binding.</summary>
public enum SamlBinding
{
    /// <summary>HTTP-Redirect.</summary>
    Redirect = 0,
    /// <summary>HTTP-POST.</summary>
    Post = 1,
}

/// <summary>SAML NameID format.</summary>
public enum SamlNameIdFormat
{
    /// <summary>Unspecified.</summary>
    Unspecified = 0,
    /// <summary>Email address.</summary>
    EmailAddress = 1,
    /// <summary>Persistent identifier (best for linking).</summary>
    Persistent = 2,
    /// <summary>Transient (changes every login: cannot be used to link users).</summary>
    Transient = 3,
}

/// <summary>Transport security of an LDAP connection. Plain LDAP is not offered.</summary>
public enum LdapSecurity
{
    /// <summary>TLS from the start (port 636).</summary>
    Ldaps = 0,
    /// <summary>A plain connection that is upgraded with StartTLS (port 389). The upgrade is required.</summary>
    StartTls = 1,
}

/// <summary>How LDAP and local passwords work together for password login in one application.</summary>
public enum LdapMode
{
    /// <summary>Only the directory.</summary>
    LdapOnly = 0,
    /// <summary>The local password first, then the directory.</summary>
    LocalThenLdap = 1,
    /// <summary>The directory first, then the local password.</summary>
    LdapThenLocal = 2,
}

/// <summary>
/// One external identity provider of an application. A base with three kinds: <see cref="OidcProviderSettings"/>,
/// <see cref="SamlProviderSettings"/> and <see cref="LdapProviderSettings"/>. Secrets are stored encrypted (fields ending in <c>Protected</c>) and are never returned.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(OidcProviderSettings), "oidc")]
[JsonDerivedType(typeof(SamlProviderSettings), "saml")]
[JsonDerivedType(typeof(LdapProviderSettings), "ldap")]
public abstract class ExternalProviderSettings
{
    /// <summary>Unique inside the application. Lowercase letters, digits and <c>-</c>. Used in URLs and as the login provider name.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string Name { get; set; } = "";
    /// <summary>The kind of provider.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public ExternalProviderType Type { get; set; }
    /// <summary>Text for the button on the login page.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? DisplayName { get; set; }
    /// <summary>On or off for this application.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool Enabled { get; set; }
    /// <summary>A new local user may be created from this source (just-in-time provisioning).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowRegister { get; set; } = true;
    /// <summary>A login may be linked to an existing user with the same <b>verified</b> email.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowLink { get; set; } = true;
    /// <summary>
    /// The email of this source counts as proof of ownership. Null means the default of the type: true for Google, Microsoft and Apple; false for the others.
    /// Only a global admin can turn it on for the types where the default is false.
    /// </summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool? TrustProviderEmailVerification { get; set; }
    /// <summary>The source's own multi-factor sign-in replaces the local two-factor step.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool TrustProviderMfa { get; set; }
    /// <summary>Source attribute name to local field (<c>email</c>, <c>given_name</c>, <c>family_name</c>, <c>name</c>, <c>phone_number</c>, <c>picture</c>).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public Dictionary<string, string> ClaimMappings { get; set; } = [];
    /// <summary>Whether group mappings are applied at login.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public GroupMappingMode GroupMappingMode { get; set; }
    /// <summary>Whether the profile is updated at every login.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public ProfileSyncMode ProfileSyncMode { get; set; }
    /// <summary>When this is the only enabled provider and no local login method is allowed, the login page goes straight to it.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AutoRedirect { get; set; }
    /// <summary>Email domains that are sent to this provider (home-realm discovery). A domain belongs to one provider per application.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> EmailDomains { get; set; } = [];
}

/// <summary>Google, Microsoft, GitHub, Apple, Facebook and generic OpenID Connect providers. Fields that do not belong to the type are ignored.</summary>
public sealed class OidcProviderSettings : ExternalProviderSettings
{
    /// <summary>Client id at the provider.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string ClientId { get; set; } = "";
    /// <summary>Client secret, encrypted. Never returned (the API shows only <c>secretIsSet</c>).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? ClientSecretProtected { get; set; }
    /// <summary>Scopes to ask for. Empty means the defaults of the type.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> Scopes { get; set; } = [];
    /// <summary>Store the provider's access and refresh tokens. Default false.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool StoreProviderTokens { get; set; }
    /// <summary>Google: only this hosted domain (<c>hd</c>).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? HostedDomain { get; set; }
    /// <summary>Microsoft: <c>common</c>, <c>organizations</c>, <c>consumers</c> or a tenant id (GUID).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? TenantId { get; set; }
    /// <summary>Microsoft: only these tenant ids may sign in (checked on the <c>tid</c> claim).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> AllowedTenants { get; set; } = [];
    /// <summary>GitHub: the user needs a verified primary email. Default true.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequireVerifiedPrimaryEmail { get; set; } = true;
    /// <summary>GitHub: the user must be a member of one of these organizations.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> AllowedOrganizations { get; set; } = [];
    /// <summary>Apple: team id.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? TeamId { get; set; }
    /// <summary>Apple: key id.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? KeyId { get; set; }
    /// <summary>Apple: the private key (PEM), encrypted. The client secret is a short-lived signed token made from it.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? PrivateKeyProtected { get; set; }
    /// <summary>Facebook: Graph API version, for example <c>v19.0</c>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? ApiVersion { get; set; }
    /// <summary>Generic OIDC: the authority (https). Its discovery document is read.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? Authority { get; set; }
    /// <summary>Generic OIDC: claim for the display name. Default <c>name</c>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? NameClaim { get; set; }
    /// <summary>Generic OIDC: claim for the email. Default <c>email</c>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? EmailClaim { get; set; }
    /// <summary>Generic OIDC: claim that holds groups.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? GroupsClaim { get; set; }
    /// <summary>Generic OIDC: issuers that are accepted besides the discovery issuer (multi-tenant providers).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> AllowedIssuers { get; set; } = [];
}

/// <summary>A SAML 2.0 identity provider. The Server is the service provider (SP).</summary>
public sealed class SamlProviderSettings : ExternalProviderSettings
{
    /// <summary>URL of the identity provider's metadata (https). Fetched with the SSRF guard and cached.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? IdpMetadataUrl { get; set; }
    /// <summary>The identity provider's metadata XML (stored as is).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? IdpMetadataXml { get; set; }
    /// <summary>Manual: entity id of the identity provider.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? IdpEntityId { get; set; }
    /// <summary>Manual: single sign-on URL (https).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? IdpSsoUrl { get; set; }
    /// <summary>Manual: single logout URL (https).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? IdpSloUrl { get; set; }
    /// <summary>Manual: signing certificates (base64 DER or PEM). Two are allowed at once for rotation.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> IdpSigningCertificates { get; set; } = [];
    /// <summary>Entity id of this application as a service provider. Empty means the generated one.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? SpEntityId { get; set; }
    /// <summary>NameID format to ask for.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public SamlNameIdFormat NameIdFormat { get; set; } = SamlNameIdFormat.Persistent;
    /// <summary>How the request is sent.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public SamlBinding AuthnRequestBinding { get; set; }
    /// <summary>Sign the authentication requests.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool SignAuthnRequests { get; set; }
    /// <summary>The assertion must be signed. Default true.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool WantAssertionsSigned { get; set; } = true;
    /// <summary>The whole response must be signed.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool WantResponseSigned { get; set; }
    /// <summary>Assertions must be encrypted.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool WantAssertionsEncrypted { get; set; }
    /// <summary>The SP certificate and private key (PFX, base64), encrypted. Made by the Server on request. Never returned.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? SpSigningCertificateProtected { get; set; }
    /// <summary>Ask the identity provider to authenticate the user again.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool ForceAuthn { get; set; }
    /// <summary>Accept responses that were not asked for (IdP-initiated). Default false. Only a global admin may turn it on.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowIdpInitiated { get; set; }
    /// <summary>Allowed clock difference in seconds. Default 60, maximum 300.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int ClockSkewSeconds { get; set; } = 60;
    /// <summary>SAML attribute name to local field.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public Dictionary<string, string> AttributeMappings { get; set; } = [];
    /// <summary>Attribute that holds groups.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? GroupsAttribute { get; set; }
    /// <summary>Attribute that holds the email. Default: the mapped <c>email</c>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? EmailAttribute { get; set; }
    /// <summary>The user's stable id comes from the <c>NameId</c> (default) or from this attribute.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? SubjectAttribute { get; set; }
    /// <summary>Optional required authentication context class.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? RequiredAuthnContext { get; set; }
    /// <summary>Allow single logout (SLO). Off by default: logout is local only.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowSingleLogout { get; set; }
}

/// <summary>An LDAP directory or Active Directory. The user's password is checked by the directory.</summary>
public sealed class LdapProviderSettings : ExternalProviderSettings
{
    /// <summary>Host name of the directory server.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string Host { get; set; } = "";
    /// <summary>Port. 636 for LDAPS, 389 for StartTLS.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int Port { get; set; } = 636;
    /// <summary>TLS mode.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public LdapSecurity Security { get; set; }
    /// <summary>Check the server certificate (chain and name). Default true. Turning it off is global-admin only and meant for tests.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool ValidateServerCertificate { get; set; } = true;
    /// <summary>A certificate authority to trust (PEM), besides the system store.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? CustomCaCertificate { get; set; }
    /// <summary>The name that must be in the server certificate. Default: <see cref="Host"/>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? TlsServerName { get; set; }
    /// <summary>The service account used to search. Anonymous bind is not allowed.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string BindDn { get; set; } = "";
    /// <summary>Password of the service account, encrypted. Never returned.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? BindPasswordProtected { get; set; }
    /// <summary>Where users are searched.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string UserSearchBase { get; set; } = "";
    /// <summary>Search filter. <c>{0}</c> (exactly once) is replaced by the escaped user name. Example: <c>(&amp;(objectClass=person)(sAMAccountName={0}))</c>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string UserSearchFilter { get; set; } = "";
    /// <summary>Attribute with the user name.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string UserNameAttribute { get; set; } = "uid";
    /// <summary>Attribute with a stable unique id (<c>objectGUID</c>, <c>entryUUID</c>). Used as the subject, never the DN.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string UniqueIdAttribute { get; set; } = "entryUUID";
    /// <summary>Attribute with the email.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? EmailAttribute { get; set; } = "mail";
    /// <summary>Attribute with the first name.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? FirstNameAttribute { get; set; } = "givenName";
    /// <summary>Attribute with the last name.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? LastNameAttribute { get; set; } = "sn";
    /// <summary>Attribute with the phone number.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? PhoneAttribute { get; set; } = "telephoneNumber";
    /// <summary>Where groups are searched. Empty: groups are read from <see cref="MemberOfAttribute"/>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? GroupSearchBase { get; set; }
    /// <summary>Filter for groups. <c>{0}</c> is replaced by the escaped DN of the user.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? GroupFilter { get; set; }
    /// <summary>Attribute of a group entry that holds the member DNs. Default <c>member</c>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string GroupMemberAttribute { get; set; } = "member";
    /// <summary>Attribute of the user entry that lists groups (for example <c>memberOf</c>).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? MemberOfAttribute { get; set; }
    /// <summary>Read groups of groups. Default false. Depth is limited to 5.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool NestedGroups { get; set; }
    /// <summary>Time limit for connect, bind and search, in seconds. Default 5.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int TimeoutSeconds { get; set; } = 5;
    /// <summary>Follow referrals. Default false.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool FollowReferrals { get; set; }
    /// <summary>How the directory and local passwords are combined.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public LdapMode Mode { get; set; }
    /// <summary>Update profile and groups at every login.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool SyncOnLogin { get; set; } = true;
    /// <summary>Show "account disabled" to the user. Default false (the answer is the same as for a wrong password).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RevealDirectoryAccountState { get; set; }
}

/// <summary>Passkey (WebAuthn) rules of one application.</summary>
public sealed class PasskeySettings
{
    /// <summary>Users may register and use passkeys in this application. Default false.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowPasskeys { get; set; }
    /// <summary><c>required</c> (default) or <c>preferred</c>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string PasskeyUserVerification { get; set; } = "required";
    /// <summary><c>preferred</c> (default), <c>required</c> or <c>discouraged</c>.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string PasskeyResidentKey { get; set; } = "preferred";
    /// <summary>Most passkeys one user may have here. Default 10.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int MaxPasskeysPerUser { get; set; } = 10;
    /// <summary>Longest passkey name. Default 50.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int PasskeyNameMaxLength { get; set; } = 50;
    /// <summary>A user without a password or other login must keep at least two passkeys (or another method) before removing one. Default true.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool RequirePasskeyBackupMethod { get; set; } = true;
}
