using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>What the Server knows about an identity provider: who it is, where to send users and which certificates sign its answers.</summary>
public sealed record SamlIdpMaterial(string EntityId, Uri SsoUrl, Uri? SloUrl, IReadOnlyList<X509Certificate2> SigningCertificates)
{
    /// <summary>Certificates that expire within the given number of days.</summary>
    public IEnumerable<X509Certificate2> ExpiringWithin(TimeSpan span, DateTimeOffset now) => SigningCertificates.Where(c => c.NotAfter.ToUniversalTime() < now.UtcDateTime + span);
}

/// <summary>Reads SAML metadata and certificates. XML is parsed without DTDs and without external resolvers (no XXE).</summary>
public static class SamlXml
{
    /// <summary>Largest accepted message or metadata document, in bytes.</summary>
    public const int MaxBytes = 512 * 1024;

    /// <summary>Safe reader settings.</summary>
    public static XmlReaderSettings SafeSettings { get; } = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = MaxBytes,
        IgnoreComments = true,
    };

    /// <summary>Parses XML safely. Throws <see cref="XmlException"/> for DTDs, oversize or bad XML.</summary>
    public static XmlDocument LoadSafe(string xml)
    {
        if (xml.Length > MaxBytes) throw new XmlException("The document is too large.");
        using var reader = XmlReader.Create(new StringReader(xml), SafeSettings);
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        doc.Load(reader);
        return doc;
    }

    /// <summary>Parses the identity provider metadata (<c>EntityDescriptor</c> with <c>IDPSSODescriptor</c>).</summary>
    public static SamlIdpMaterial ParseIdpMetadata(string xml)
    {
        XDocument doc;
        try
        {
            if (xml.Length > MaxBytes) throw new XmlException("The document is too large.");
            using var reader = XmlReader.Create(new StringReader(xml), SafeSettings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex) { throw new InvalidOperationException("The metadata is not valid XML.", ex); }

        XNamespace md = "urn:oasis:names:tc:SAML:2.0:metadata", ds = "http://www.w3.org/2000/09/xmldsig#";
        var entity = doc.Descendants(md + "EntityDescriptor").FirstOrDefault() ?? throw new InvalidOperationException("No EntityDescriptor.");
        var idp = entity.Element(md + "IDPSSODescriptor") ?? throw new InvalidOperationException("No IDPSSODescriptor.");
        var entityId = (string?)entity.Attribute("entityID") ?? throw new InvalidOperationException("No entityID.");

        Uri? Service(string element, params string[] bindings)
        {
            var services = idp.Elements(md + element).ToList();
            foreach (var binding in bindings)
                if (services.FirstOrDefault(s => ((string?)s.Attribute("Binding"))?.EndsWith(binding, StringComparison.Ordinal) == true) is { } hit && (string?)hit.Attribute("Location") is { } loc)
                    return new Uri(loc);
            return null;
        }
        var sso = Service("SingleSignOnService", "HTTP-Redirect", "HTTP-POST") ?? throw new InvalidOperationException("No SingleSignOnService.");
        var slo = Service("SingleLogoutService", "HTTP-Redirect", "HTTP-POST");
        var certs = new List<X509Certificate2>();
        foreach (var key in idp.Elements(md + "KeyDescriptor").Where(k => ((string?)k.Attribute("use")) is null or "signing"))
            foreach (var x in key.Descendants(ds + "X509Certificate"))
                certs.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(string.Concat(x.Value.Where(c => !char.IsWhiteSpace(c))))));
        if (certs.Count == 0) throw new InvalidOperationException("No signing certificate.");
        return new SamlIdpMaterial(entityId, sso, slo, certs);
    }

    /// <summary>Reads a certificate given as PEM or base64 DER.</summary>
    public static X509Certificate2 LoadCertificate(string value) =>
        X509CertificateLoader.LoadCertificate(Application.ExternalProviderSettingsValidator.DecodeCertificate(value));
}

/// <summary>Finds the material of an identity provider from settings: metadata URL (cached, refreshed), metadata XML, or manual fields.</summary>
public interface ISamlProviderMaterial
{
    /// <summary>The material for the provider. Throws <see cref="ExternalLoginException"/> when it cannot be built.</summary>
    Task<SamlIdpMaterial> GetAsync(RegisteredProvider provider, CancellationToken ct);
}

/// <inheritdoc />
public sealed class SamlProviderMaterial(IHttpClientFactory http, IMemoryCache cache, IOptions<Auth2ServerOptions> options, ILogger<SamlProviderMaterial> log) : ISamlProviderMaterial
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);

    /// <inheritdoc />
    public async Task<SamlIdpMaterial> GetAsync(RegisteredProvider provider, CancellationToken ct)
    {
        var s = (SamlProviderSettings)provider.Settings;
        try
        {
            if (!string.IsNullOrWhiteSpace(s.IdpMetadataXml)) return SamlXml.ParseIdpMetadata(s.IdpMetadataXml);
            if (!string.IsNullOrWhiteSpace(s.IdpMetadataUrl)) return await FromUrlAsync(new Uri(s.IdpMetadataUrl), ct);
            return new SamlIdpMaterial(s.IdpEntityId!, new Uri(s.IdpSsoUrl!), string.IsNullOrWhiteSpace(s.IdpSloUrl) ? null : new Uri(s.IdpSloUrl),
                [.. s.IdpSigningCertificates.Select(SamlXml.LoadCertificate)]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or CryptographicException or UriFormatException or HttpRequestException or ArgumentNullException)
        {
            log.LogWarning(ex, "The SAML identity provider material of {Provider} cannot be read.", provider.RegistrationName);
            throw new ExternalLoginException(Auth2ErrorCodes.SamlResponseInvalid, "saml_idp_material_invalid", ex);
        }
    }

    private async Task<SamlIdpMaterial> FromUrlAsync(Uri url, CancellationToken ct)
    {
        SsrfGuard.EnsureAllowedUrl(url, options.Value.ExternalProviders.AllowPrivateNetworks);
        var freshKey = "saml-idp-meta:" + url;
        if (cache.TryGetValue(freshKey, out SamlIdpMaterial? hit) && hit is not null) return hit;
        var staleKey = freshKey + ":stale";
        try
        {
            var xml = await http.CreateClient(ExternalHttp.ClientName).GetStringAsync(url, ct);
            var material = SamlXml.ParseIdpMetadata(xml);
            cache.Set(freshKey, material, Fresh);
            cache.Set(staleKey, material);   // the last good copy, used when a refresh fails
            return material;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            if (cache.TryGetValue(staleKey, out SamlIdpMaterial? old) && old is not null)
            {
                log.LogWarning("The metadata refresh of {Url} failed. The last good copy is used.", url.Host);
                cache.Set(freshKey, old, TimeSpan.FromMinutes(5));
                return old;
            }
            throw;
        }
    }
}

/// <summary>Makes and reads the certificate of this Server as a SAML service provider.</summary>
public static class SamlSpCertificates
{
    /// <summary>Makes a self-signed RSA 2048 certificate. Returns the PFX (base64) and the expiry. Encrypt the PFX before storing it.</summary>
    public static (string PfxBase64, DateTimeOffset NotAfter) Generate(string commonName, DateTimeOffset now, int years = 3)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + commonName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(years));
        return (Convert.ToBase64String(cert.Export(X509ContentType.Pfx)), cert.NotAfter);
    }

    /// <summary>Reads the PFX made by <see cref="Generate"/>.</summary>
    public static X509Certificate2 Load(string pfxBase64) =>
        X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(pfxBase64), null, X509KeyStorageFlags.Exportable);
}
