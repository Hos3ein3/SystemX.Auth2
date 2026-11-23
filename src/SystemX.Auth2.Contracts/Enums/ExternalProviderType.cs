using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>The kinds of external identity source. Passkeys are not a provider: they are a credential of the user.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExternalProviderType>))]
public enum ExternalProviderType
{
    /// <summary>Google (OpenID Connect).</summary>
    Google,
    /// <summary>Microsoft account or Entra ID (OpenID Connect).</summary>
    Microsoft,
    /// <summary>GitHub (OAuth 2.0).</summary>
    GitHub,
    /// <summary>Sign in with Apple.</summary>
    Apple,
    /// <summary>Facebook Login (OAuth 2.0).</summary>
    Facebook,
    /// <summary>Any OpenID Connect provider with a discovery document.</summary>
    GenericOidc,
    /// <summary>A SAML 2.0 identity provider. The server is the service provider.</summary>
    Saml,
    /// <summary>An LDAP directory or Active Directory (user name and password are checked against it).</summary>
    Ldap,
}
