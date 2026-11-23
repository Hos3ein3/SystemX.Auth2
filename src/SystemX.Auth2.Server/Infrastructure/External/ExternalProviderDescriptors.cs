using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>The kind of a settings field. A future admin UI draws a form from these.</summary>
public enum ProviderFieldKind
{
    /// <summary>One line of text.</summary>
    Text = 0,
    /// <summary>A secret. Written, never read.</summary>
    Secret = 1,
    /// <summary>One value of a list.</summary>
    Select = 2,
    /// <summary>A list of texts.</summary>
    List = 3,
    /// <summary>On or off.</summary>
    Bool = 4,
    /// <summary>A certificate (PEM or base64).</summary>
    Certificate = 5,
    /// <summary>Key and value pairs.</summary>
    Map = 6,
}

/// <summary>Describes one settings field of a provider type. <c>LabelKey</c> and <c>HelpKey</c> are resource keys.</summary>
public sealed record ProviderField(string Name, string LabelKey, ProviderFieldKind Kind, bool Required, string? Default = null, string? HelpKey = null);

/// <summary>Everything the Server and a future admin UI need to know about one provider type, in one place. Add a type by adding a descriptor.</summary>
public interface IExternalProviderDescriptor
{
    /// <summary>The type.</summary>
    ExternalProviderType Type { get; }
    /// <summary>Name for people.</summary>
    string DisplayName { get; }
    /// <summary>The settings class of this type.</summary>
    Type SettingsType { get; }
    /// <summary>Fields of this type (besides the common ones).</summary>
    IReadOnlyList<ProviderField> Fields { get; }
    /// <summary>Default scopes (OIDC only).</summary>
    IReadOnlyList<string> DefaultScopes { get; }
    /// <summary>Anchor of the setup guide, for example <c>external-identity/google.md</c>.</summary>
    string SetupDocsAnchor { get; }
    /// <summary>The email of this type counts as verified by default.</summary>
    bool EmailVerifiedByDefault { get; }
    /// <summary>The text that tells the admin which URLs to enter at the provider.</summary>
    string BuildRedirectInfo(ExternalProviderSettings settings, string issuer, string projectSlug);
}

/// <summary>Descriptors of all provider types.</summary>
public interface IExternalProviderDescriptors
{
    /// <summary>All descriptors.</summary>
    IReadOnlyList<IExternalProviderDescriptor> All { get; }
    /// <summary>The descriptor of a type.</summary>
    IExternalProviderDescriptor For(ExternalProviderType type);
}

/// <inheritdoc />
public sealed class ExternalProviderDescriptors(IEnumerable<IExternalProviderDescriptor> all) : IExternalProviderDescriptors
{
    private readonly Dictionary<ExternalProviderType, IExternalProviderDescriptor> _map = all.ToDictionary(d => d.Type);

    /// <inheritdoc />
    public IReadOnlyList<IExternalProviderDescriptor> All => [.. _map.Values];

    /// <inheritdoc />
    public IExternalProviderDescriptor For(ExternalProviderType type) =>
        _map.TryGetValue(type, out var d) ? d : throw new ArgumentOutOfRangeException(nameof(type), type, "No descriptor for this provider type.");
}

/// <summary>Base for the OIDC-style descriptors.</summary>
public abstract class OidcDescriptor : IExternalProviderDescriptor
{
    /// <inheritdoc />
    public abstract ExternalProviderType Type { get; }
    /// <inheritdoc />
    public abstract string DisplayName { get; }
    /// <inheritdoc />
    public Type SettingsType => typeof(OidcProviderSettings);
    /// <inheritdoc />
    public virtual IReadOnlyList<ProviderField> Fields { get; } = [];
    /// <inheritdoc />
    public virtual IReadOnlyList<string> DefaultScopes => ["openid", "email", "profile"];
    /// <inheritdoc />
    public virtual string SetupDocsAnchor => $"external-identity/{Type.ToString().ToLowerInvariant()}.md";
    /// <inheritdoc />
    public virtual bool EmailVerifiedByDefault => false;
    /// <inheritdoc />
    public virtual string BuildRedirectInfo(ExternalProviderSettings s, string issuer, string projectSlug) =>
        $"Redirect URI: {issuer.TrimEnd('/')}/callback/login/{projectSlug}/{s.Name}";

    /// <summary>The common fields of an OIDC client.</summary>
    protected static IEnumerable<ProviderField> Client() =>
    [
        new("ClientId", "field.client_id", ProviderFieldKind.Text, true),
        new("ClientSecret", "field.client_secret", ProviderFieldKind.Secret, true),
        new("Scopes", "field.scopes", ProviderFieldKind.List, false),
        new("StoreProviderTokens", "field.store_provider_tokens", ProviderFieldKind.Bool, false, "false", "help.store_provider_tokens"),
    ];
}

internal sealed class GoogleDescriptor : OidcDescriptor
{
    public override ExternalProviderType Type => ExternalProviderType.Google;
    public override string DisplayName => "Google";
    public override IReadOnlyList<ProviderField> Fields { get; } = [.. Client(), new("HostedDomain", "field.hosted_domain", ProviderFieldKind.Text, false, null, "help.hosted_domain")];
    public override bool EmailVerifiedByDefault => true;
}

internal sealed class MicrosoftDescriptor : OidcDescriptor
{
    public override ExternalProviderType Type => ExternalProviderType.Microsoft;
    public override string DisplayName => "Microsoft";
    public override IReadOnlyList<ProviderField> Fields { get; } =
        [.. Client(), new("TenantId", "field.tenant_id", ProviderFieldKind.Text, false, "common"), new("AllowedTenants", "field.allowed_tenants", ProviderFieldKind.List, false)];
    public override bool EmailVerifiedByDefault => true;
}

internal sealed class GitHubDescriptor : OidcDescriptor
{
    public override ExternalProviderType Type => ExternalProviderType.GitHub;
    public override string DisplayName => "GitHub";
    public override IReadOnlyList<string> DefaultScopes => ["read:user", "user:email"];
    public override IReadOnlyList<ProviderField> Fields { get; } =
        [.. Client(), new("RequireVerifiedPrimaryEmail", "field.require_verified_primary_email", ProviderFieldKind.Bool, false, "true"), new("AllowedOrganizations", "field.allowed_organizations", ProviderFieldKind.List, false)];
}

internal sealed class AppleDescriptor : OidcDescriptor
{
    public override ExternalProviderType Type => ExternalProviderType.Apple;
    public override string DisplayName => "Apple";
    public override IReadOnlyList<string> DefaultScopes => ["name", "email"];
    public override IReadOnlyList<ProviderField> Fields { get; } =
    [
        new("ClientId", "field.apple_services_id", ProviderFieldKind.Text, true),
        new("TeamId", "field.team_id", ProviderFieldKind.Text, true),
        new("KeyId", "field.key_id", ProviderFieldKind.Text, true),
        new("PrivateKey", "field.private_key", ProviderFieldKind.Secret, true),
    ];
    public override bool EmailVerifiedByDefault => true;
}

internal sealed class FacebookDescriptor : OidcDescriptor
{
    public override ExternalProviderType Type => ExternalProviderType.Facebook;
    public override string DisplayName => "Facebook";
    public override IReadOnlyList<string> DefaultScopes => ["public_profile", "email"];
    public override IReadOnlyList<ProviderField> Fields { get; } = [.. Client(), new("ApiVersion", "field.api_version", ProviderFieldKind.Text, false, "v19.0")];
}

internal sealed class GenericOidcDescriptor : OidcDescriptor
{
    public override ExternalProviderType Type => ExternalProviderType.GenericOidc;
    public override string DisplayName => "OpenID Connect";
    public override IReadOnlyList<ProviderField> Fields { get; } =
    [
        .. Client(),
        new("Authority", "field.authority", ProviderFieldKind.Text, true),
        new("NameClaim", "field.name_claim", ProviderFieldKind.Text, false, "name"),
        new("EmailClaim", "field.email_claim", ProviderFieldKind.Text, false, "email"),
        new("GroupsClaim", "field.groups_claim", ProviderFieldKind.Text, false),
        new("AllowedIssuers", "field.allowed_issuers", ProviderFieldKind.List, false),
    ];
    public override string SetupDocsAnchor => "external-identity/generic-oidc.md";
}

internal sealed class SamlDescriptor : IExternalProviderDescriptor
{
    public ExternalProviderType Type => ExternalProviderType.Saml;
    public string DisplayName => "SAML 2.0";
    public Type SettingsType => typeof(SamlProviderSettings);
    public IReadOnlyList<string> DefaultScopes => [];
    public string SetupDocsAnchor => "external-identity/saml-generic.md";
    public bool EmailVerifiedByDefault => false;
    public IReadOnlyList<ProviderField> Fields { get; } =
    [
        new("IdpMetadataUrl", "field.idp_metadata_url", ProviderFieldKind.Text, false),
        new("IdpMetadataXml", "field.idp_metadata_xml", ProviderFieldKind.Text, false),
        new("IdpEntityId", "field.idp_entity_id", ProviderFieldKind.Text, false),
        new("IdpSsoUrl", "field.idp_sso_url", ProviderFieldKind.Text, false),
        new("IdpSigningCertificates", "field.idp_signing_certificates", ProviderFieldKind.Certificate, false),
        new("SpEntityId", "field.sp_entity_id", ProviderFieldKind.Text, false),
        new("NameIdFormat", "field.name_id_format", ProviderFieldKind.Select, false, "Persistent"),
        new("SignAuthnRequests", "field.sign_authn_requests", ProviderFieldKind.Bool, false, "false"),
        new("WantAssertionsSigned", "field.want_assertions_signed", ProviderFieldKind.Bool, false, "true"),
        new("AttributeMappings", "field.attribute_mappings", ProviderFieldKind.Map, false),
        new("GroupsAttribute", "field.groups_attribute", ProviderFieldKind.Text, false),
    ];
    public string BuildRedirectInfo(ExternalProviderSettings s, string issuer, string projectSlug)
    {
        var b = $"{issuer.TrimEnd('/')}/saml/{projectSlug}/{s.Name}";
        var entity = (s as SamlProviderSettings)?.SpEntityId is { Length: > 0 } e ? e : b + "/metadata";
        return $"ACS URL: {b}/acs\nSP entity id: {entity}\nMetadata: {b}/metadata\nSingle logout URL: {b}/slo";
    }
}

internal sealed class LdapDescriptor : IExternalProviderDescriptor
{
    public ExternalProviderType Type => ExternalProviderType.Ldap;
    public string DisplayName => "LDAP / Active Directory";
    public Type SettingsType => typeof(LdapProviderSettings);
    public IReadOnlyList<string> DefaultScopes => [];
    public string SetupDocsAnchor => "external-identity/ldap-openldap.md";
    public bool EmailVerifiedByDefault => false;
    public IReadOnlyList<ProviderField> Fields { get; } =
    [
        new("Host", "field.host", ProviderFieldKind.Text, true),
        new("Port", "field.port", ProviderFieldKind.Text, true, "636"),
        new("Security", "field.security", ProviderFieldKind.Select, true, "Ldaps"),
        new("BindDn", "field.bind_dn", ProviderFieldKind.Text, true),
        new("BindPassword", "field.bind_password", ProviderFieldKind.Secret, true),
        new("UserSearchBase", "field.user_search_base", ProviderFieldKind.Text, true),
        new("UserSearchFilter", "field.user_search_filter", ProviderFieldKind.Text, true, null, "help.user_search_filter"),
        new("UniqueIdAttribute", "field.unique_id_attribute", ProviderFieldKind.Text, true, "entryUUID"),
        new("GroupSearchBase", "field.group_search_base", ProviderFieldKind.Text, false),
        new("GroupFilter", "field.group_filter", ProviderFieldKind.Text, false),
        new("Mode", "field.ldap_mode", ProviderFieldKind.Select, false, "LdapOnly"),
    ];
    public string BuildRedirectInfo(ExternalProviderSettings s, string issuer, string projectSlug) =>
        "No URL is needed at the directory. Allow this server to connect to the host and port.";
}
