using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.IdentityModel.Tokens.Saml2;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>What one fake SAML response contains. Every property is a way to be correct or wrong.</summary>
public sealed class SamlResponseOptions
{
    public string NameId { get; set; } = "user-1";
    public string? InResponseTo { get; set; }
    public string? Audience { get; set; }
    public string? Destination { get; set; }
    public string? Issuer { get; set; }
    public int IssuedTokenLifetimeMinutes { get; set; } = 10;
    public int ConfirmationLifetimeMinutes { get; set; } = 5;
    public bool SignAssertion { get; set; } = true;
    public bool SignResponse { get; set; }
    /// <summary>Remove every signature after the response is built (a fully unsigned message).</summary>
    public bool StripSignatures { get; set; }
    public X509Certificate2? SignWith { get; set; }
    public X509Certificate2? EncryptTo { get; set; }
    public Dictionary<string, string[]> Attributes { get; set; } = new() { ["email"] = ["sara@example.org"], ["givenname"] = ["Sara"], ["surname"] = ["Test"], ["groups"] = ["finance", "staff"] };
}

/// <summary>An in-memory SAML identity provider: metadata over HTTP, and signed (or deliberately broken) responses.</summary>
public sealed class FakeSamlIdp : HttpMessageHandler
{
    public const string EntityId = "https://idp.test/entity";
    public const string SsoUrl = "https://idp.test/sso";
    public const string MetadataUrl = "https://idp.test/metadata";

    public X509Certificate2 Signing { get; } = Cert("idp-signing");
    public List<X509Certificate2> PublishedCertificates { get; } = [];
    public int MetadataRequests { get; private set; }
    public bool MetadataDown { get; set; }

    public FakeSamlIdp() => PublishedCertificates.Add(Signing);

    public static X509Certificate2 Cert(string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    public string MetadataXml() => $"""
        <EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{EntityId}">
          <IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
            {string.Concat(PublishedCertificates.Select(c => $"<KeyDescriptor use=\"signing\"><KeyInfo xmlns=\"http://www.w3.org/2000/09/xmldsig#\"><X509Data><X509Certificate>{Convert.ToBase64String(c.RawData)}</X509Certificate></X509Data></KeyInfo></KeyDescriptor>"))}
            <SingleLogoutService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect" Location="https://idp.test/slo"/>
            <SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect" Location="{SsoUrl}"/>
          </IDPSSODescriptor>
        </EntityDescriptor>
        """;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.ToString() == MetadataUrl)
        {
            MetadataRequests++;
            if (MetadataDown) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MetadataXml(), Encoding.UTF8, "application/xml") });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>The AuthnRequest in the redirect URL of the Server.</summary>
    public static (XmlDocument Xml, string Id, string RelayState) ReadRequest(Uri redirect)
    {
        var q = HttpUtility.ParseQueryString(redirect.Query);
        using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(q["SAMLRequest"]!)), CompressionMode.Decompress);
        var xml = new XmlDocument();
        xml.Load(inflate);
        return (xml, xml.DocumentElement!.GetAttribute("ID"), q["RelayState"]!);
    }

    private Saml2Configuration IdpConfig(SamlResponseOptions o) => new()
    {
        Issuer = o.Issuer ?? EntityId,
        SigningCertificate = o.SignWith ?? Signing,
        SignatureAlgorithm = Saml2SecurityAlgorithms.RsaSha256Signature,
        EncryptionCertificate = o.EncryptTo,
        CertificateValidationMode = X509CertificateValidationMode.None,
        RevocationMode = X509RevocationMode.NoCheck,
        AuthnResponseSignType = o.SignResponse && o.SignAssertion ? Saml2AuthnResponseSignTypes.SignAssertionAndResponse
            : o.SignResponse ? Saml2AuthnResponseSignTypes.SignResponse : Saml2AuthnResponseSignTypes.SignAssertion,
    };

    /// <summary>A response as an identity provider sends it. Returns the base64 value of the <c>SAMLResponse</c> field.</summary>
    public string Response(string acsUrl, string spEntityId, SamlResponseOptions o)
    {
        var claims = o.Attributes.SelectMany(kv => kv.Value.Select(v => new Claim(kv.Key, v))).ToList();
        var response = new Saml2AuthnResponse(IdpConfig(o))
        {
            Status = Saml2StatusCodes.Success,
            Destination = new Uri(o.Destination ?? acsUrl),
            InResponseTo = o.InResponseTo is null ? null : new Saml2Id(o.InResponseTo),
            ClaimsIdentity = new ClaimsIdentity(claims),
            NameId = new Saml2NameIdentifier(o.NameId, NameIdentifierFormats.Persistent),
        };
        response.CreateSecurityToken(o.Audience ?? spEntityId, subjectConfirmationLifetime: o.ConfirmationLifetimeMinutes, issuedTokenLifetime: o.IssuedTokenLifetimeMinutes);
        var binding = new Saml2PostBinding();
        binding.Bind(response);
        var b64 = Regex.Match(binding.PostContent, "name=\"SAMLResponse\" value=\"([^\"]+)\"").Groups[1].Value;
        if (!o.StripSignatures) return b64;
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(Decode(b64));
        foreach (var sig in doc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#").Cast<XmlNode>().ToList()) sig.ParentNode!.RemoveChild(sig);
        return Encode(doc.OuterXml);
    }

    public static string Encode(string xml) => Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));
    public static string Decode(string b64) => Encoding.UTF8.GetString(Convert.FromBase64String(b64));
}
