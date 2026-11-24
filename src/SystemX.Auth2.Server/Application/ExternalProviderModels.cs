using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>
/// An enabled provider as the Server uses it at runtime. Secrets stay encrypted inside <see cref="Settings"/>
/// and are decrypted only at the moment of use (by the external secret protector).
/// </summary>
public sealed record RegisteredProvider(
    Guid ProjectId, string ProjectSlug, ExternalProviderSettings Settings, int SettingsVersion)
{
    /// <summary>Provider name inside the application.</summary>
    public string Name => Settings.Name;
    /// <summary>Provider type.</summary>
    public ExternalProviderType Type => Settings.Type;
    /// <summary>Globally unique registration name: <c>ext-{applicationId:N}-{name}</c>.</summary>
    public string RegistrationName => ExternalProviderUrls.RegistrationName(ProjectId, Name);
}

/// <summary>Builds the names and URLs of providers. URLs are unique per application and provider (protection against mix-up attacks).</summary>
public static class ExternalProviderUrls
{
    /// <summary>The project slug used in URLs: the project key in multi mode, <c>default</c> in single mode.</summary>
    public const string SingleModeSlug = "default";

    /// <summary>Registration name that is unique in the whole server.</summary>
    public static string RegistrationName(Guid applicationId, string name) => $"ext-{applicationId:N}-{name}";

    /// <summary>Path of the OIDC callback.</summary>
    public static string OidcCallbackPath(string projectSlug, string name) => $"/callback/login/{projectSlug}/{name}";
    /// <summary>Path of the SAML assertion consumer service.</summary>
    public static string SamlAcsPath(string projectSlug, string name) => $"/saml/{projectSlug}/{name}/acs";
    /// <summary>Path of the SAML single logout service.</summary>
    public static string SamlSloPath(string projectSlug, string name) => $"/saml/{projectSlug}/{name}/slo";
    /// <summary>Path of the SAML metadata.</summary>
    public static string SamlMetadataPath(string projectSlug, string name) => $"/saml/{projectSlug}/{name}/metadata";

    /// <summary>The full URLs the admin must enter at the provider.</summary>
    public static IReadOnlyDictionary<string, string> For(RegisteredProvider p, string issuer)
    {
        var root = issuer.TrimEnd('/');
        return p.Type switch
        {
            ExternalProviderType.Saml => new Dictionary<string, string>
            {
                ["acs"] = root + SamlAcsPath(p.ProjectSlug, p.Name),
                ["slo"] = root + SamlSloPath(p.ProjectSlug, p.Name),
                ["metadata"] = root + SamlMetadataPath(p.ProjectSlug, p.Name),
            },
            ExternalProviderType.Ldap => new Dictionary<string, string>(),
            _ => new Dictionary<string, string> { ["callback"] = root + OidcCallbackPath(p.ProjectSlug, p.Name) },
        };
    }
}
