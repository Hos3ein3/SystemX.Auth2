using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>Checks the external providers and the passkey rules of one application.</summary>
public static partial class ExternalProviderSettingsValidator
{
    /// <summary>Most providers one application may have.</summary>
    public const int MaxProviders = 20;

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex NamePattern();

    /// <summary>Validates the provider list and the passkey settings.</summary>
    public static void Validate(ProjectSettings s, Action<string, string> err)
    {
        var list = s.ExternalProviders;
        if (list.Count > MaxProviders) err("ExternalProviders", $"At most {MaxProviders} providers are allowed.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var domains = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Count; i++)
        {
            var x = list[i];
            var p = $"ExternalProviders[{i}]";
            if (!NamePattern().IsMatch(x.Name ?? "")) err($"{p}.Name", "Use 1-40 lowercase letters, digits or '-'. Do not start or end with '-'.");
            else if (!names.Add(x.Name!)) err($"{p}.Name", "Must be unique in the application.");

            foreach (var d in x.EmailDomains)
            {
                if (string.IsNullOrWhiteSpace(d) || d.Contains('@') || d.Contains(' ') || !d.Contains('.'))
                    err($"{p}.EmailDomains", $"'{d}' is not a domain name.");
                else if (domains.TryGetValue(d, out var other) && other != x.Name)
                    err($"{p}.EmailDomains", $"'{d}' already belongs to provider '{other}'.");
                else domains[d] = x.Name ?? "";
            }

            switch (x)
            {
                case OidcProviderSettings o: Oidc(o, p, err); break;
                case SamlProviderSettings m: Saml(m, p, err); break;
                case LdapProviderSettings l: Ldap(l, p, err); break;
            }
        }

        var k = s.Passkeys;
        if (k.PasskeyUserVerification is not ("required" or "preferred")) err("Passkeys.PasskeyUserVerification", "Use 'required' or 'preferred'.");
        if (k.PasskeyResidentKey is not ("required" or "preferred" or "discouraged")) err("Passkeys.PasskeyResidentKey", "Use 'required', 'preferred' or 'discouraged'.");
        if (k.MaxPasskeysPerUser is < 1 or > 50) err("Passkeys.MaxPasskeysPerUser", "Must be between 1 and 50.");
        if (k.PasskeyNameMaxLength is < 1 or > 200) err("Passkeys.PasskeyNameMaxLength", "Must be between 1 and 200.");
    }

    private static bool IsHttps(string? v) => Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo);

    private static void Oidc(OidcProviderSettings o, string p, Action<string, string> err)
    {
        if (o.Type is ExternalProviderType.Saml or ExternalProviderType.Ldap) { err($"{p}.Type", "An OIDC provider cannot have this type."); return; }
        if (!o.Enabled) return;
        if (string.IsNullOrWhiteSpace(o.ClientId)) err($"{p}.ClientId", "Is required when enabled.");
        if (o.Type != ExternalProviderType.Apple && string.IsNullOrEmpty(o.ClientSecretProtected))
            err($"{p}.ClientSecret", "Is required when enabled.");
        switch (o.Type)
        {
            case ExternalProviderType.GenericOidc:
                if (!IsHttps(o.Authority)) err($"{p}.Authority", "Must be an https URL.");
                foreach (var iss in o.AllowedIssuers) if (!IsHttps(iss)) err($"{p}.AllowedIssuers", $"'{iss}' must be an https URL.");
                break;
            case ExternalProviderType.Apple:
                if (string.IsNullOrWhiteSpace(o.TeamId)) err($"{p}.TeamId", "Is required for Apple.");
                if (string.IsNullOrWhiteSpace(o.KeyId)) err($"{p}.KeyId", "Is required for Apple.");
                if (string.IsNullOrEmpty(o.PrivateKeyProtected)) err($"{p}.PrivateKey", "Is required for Apple.");
                break;
            case ExternalProviderType.Microsoft:
                if (o.TenantId is not null && o.TenantId is not ("common" or "organizations" or "consumers") && !Guid.TryParse(o.TenantId, out _))
                    err($"{p}.TenantId", "Use common, organizations, consumers or a tenant id (GUID).");
                foreach (var t in o.AllowedTenants) if (!Guid.TryParse(t, out _)) err($"{p}.AllowedTenants", $"'{t}' is not a tenant id (GUID).");
                break;
            case ExternalProviderType.Facebook:
                if (o.ApiVersion is not null && !Regex.IsMatch(o.ApiVersion, @"^v\d{1,2}\.\d$")) err($"{p}.ApiVersion", "Use a version like v19.0.");
                break;
        }
    }

    private static void Saml(SamlProviderSettings m, string p, Action<string, string> err)
    {
        if (m.Type != ExternalProviderType.Saml) { err($"{p}.Type", "A SAML provider must have the type Saml."); return; }
        if (m.ClockSkewSeconds is < 0 or > 300) err($"{p}.ClockSkewSeconds", "Must be between 0 and 300.");
        if (!m.Enabled) return;
        var hasMeta = !string.IsNullOrWhiteSpace(m.IdpMetadataUrl) || !string.IsNullOrWhiteSpace(m.IdpMetadataXml);
        if (!string.IsNullOrWhiteSpace(m.IdpMetadataUrl) && !IsHttps(m.IdpMetadataUrl)) err($"{p}.IdpMetadataUrl", "Must be an https URL.");
        if (!hasMeta)
        {
            if (string.IsNullOrWhiteSpace(m.IdpEntityId)) err($"{p}.IdpEntityId", "Is required without metadata.");
            if (!IsHttps(m.IdpSsoUrl)) err($"{p}.IdpSsoUrl", "Must be an https URL.");
            if (m.IdpSigningCertificates.Count == 0) err($"{p}.IdpSigningCertificates", "At least one signing certificate is needed without metadata.");
        }
        if (!string.IsNullOrWhiteSpace(m.IdpSloUrl) && !IsHttps(m.IdpSloUrl)) err($"{p}.IdpSloUrl", "Must be an https URL.");
        if (m.IdpSigningCertificates.Count > 2) err($"{p}.IdpSigningCertificates", "At most two certificates (current and next).");
        foreach (var c in m.IdpSigningCertificates)
        {
            try
            {
                using var cert = X509CertificateLoader.LoadCertificate(DecodeCertificate(c));
                if (cert.NotAfter < DateTime.UtcNow) err($"{p}.IdpSigningCertificates", "A certificate has expired.");
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
            {
                err($"{p}.IdpSigningCertificates", "A certificate cannot be read. Use base64 DER or PEM.");
            }
        }
        if (!m.WantAssertionsSigned && !m.WantResponseSigned)
            err($"{p}.WantAssertionsSigned", "Either the assertion or the response must be signed.");
        if (m.SignAuthnRequests && string.IsNullOrEmpty(m.SpSigningCertificateProtected))
            err($"{p}.SignAuthnRequests", "Needs an SP certificate. Create one first.");
    }

    /// <summary>Reads base64 DER or PEM into bytes.</summary>
    public static byte[] DecodeCertificate(string value)
    {
        var text = string.Join("", value.Split('\n', '\r').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("-----", StringComparison.Ordinal)));
        return Convert.FromBase64String(text);
    }

    private static void Ldap(LdapProviderSettings l, string p, Action<string, string> err)
    {
        if (l.Type != ExternalProviderType.Ldap) { err($"{p}.Type", "An LDAP provider must have the type Ldap."); return; }
        if (l.Port is < 1 or > 65535) err($"{p}.Port", "Must be between 1 and 65535.");
        if (l.TimeoutSeconds is < 1 or > 60) err($"{p}.TimeoutSeconds", "Must be between 1 and 60.");
        if (!l.Enabled) return;
        if (string.IsNullOrWhiteSpace(l.Host)) err($"{p}.Host", "Is required.");
        if (string.IsNullOrWhiteSpace(l.BindDn)) err($"{p}.BindDn", "Is required. Anonymous bind is not allowed.");
        if (string.IsNullOrEmpty(l.BindPasswordProtected)) err($"{p}.BindPassword", "Is required. An empty password is not allowed.");
        if (string.IsNullOrWhiteSpace(l.UserSearchBase)) err($"{p}.UserSearchBase", "Is required.");
        if (CountPlaceholder(l.UserSearchFilter) != 1) err($"{p}.UserSearchFilter", "Must contain {0} exactly once.");
        if (string.IsNullOrWhiteSpace(l.UniqueIdAttribute)) err($"{p}.UniqueIdAttribute", "Is required.");
        if (!string.IsNullOrWhiteSpace(l.GroupFilter) && CountPlaceholder(l.GroupFilter) != 1) err($"{p}.GroupFilter", "Must contain {0} exactly once.");
        if (!string.IsNullOrWhiteSpace(l.GroupFilter) && string.IsNullOrWhiteSpace(l.GroupSearchBase)) err($"{p}.GroupSearchBase", "Is required with a group filter.");
        if (!string.IsNullOrWhiteSpace(l.CustomCaCertificate))
        {
            try { using var _ = X509CertificateLoader.LoadCertificate(DecodeCertificate(l.CustomCaCertificate)); }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
            { err($"{p}.CustomCaCertificate", "The certificate cannot be read. Use PEM or base64 DER."); }
        }
    }

    private static int CountPlaceholder(string? filter)
    {
        if (string.IsNullOrEmpty(filter)) return 0;
        var n = 0;
        for (var i = filter.IndexOf("{0}", StringComparison.Ordinal); i >= 0; i = filter.IndexOf("{0}", i + 3, StringComparison.Ordinal)) n++;
        return n;
    }
}
