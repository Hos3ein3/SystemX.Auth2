using System.Collections.Specialized;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using System.Text.RegularExpressions;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.IdentityModel.Tokens.Saml2;

namespace SystemX.Auth2.Server.IntegrationTests.Spikes;

public static class SamlSpikeKit
{
    public static X509Certificate2 NewCertificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    public static Saml2Configuration IdpConfig(X509Certificate2 signing) => new()
    {
        Issuer = "https://idp.test/entity", SigningCertificate = signing, SignatureAlgorithm = Saml2SecurityAlgorithms.RsaSha256Signature,
        CertificateValidationMode = X509CertificateValidationMode.None, RevocationMode = X509RevocationMode.NoCheck,
    };

    public static Saml2Configuration SpConfig(params X509Certificate2[] trusted)
    {
        var c = new Saml2Configuration
        {
            Issuer = "https://sp.test/entity", CertificateValidationMode = X509CertificateValidationMode.None, RevocationMode = X509RevocationMode.NoCheck,
            AudienceRestricted = true,
        };
        c.AllowedAudienceUris.Add("https://sp.test/entity");
        foreach (var t in trusted) c.SignatureValidationCertificates.Add(t);
        return c;
    }

    /// <summary>A SAML response as an identity provider sends it. Returns the base64 value of the form field.</summary>
    public static string Response(Saml2Configuration idp, string acs, string? inResponseTo, string nameId = "user-1", string audience = "https://sp.test/entity")
    {
        var response = new Saml2AuthnResponse(idp)
        {
            Status = Saml2StatusCodes.Success, Destination = new Uri(acs),
            InResponseTo = inResponseTo is null ? null : new Saml2Id(inResponseTo),
            ClaimsIdentity = new ClaimsIdentity([new Claim("email", "sara@example.org"), new Claim("groups", "finance")]),
            NameId = new Saml2NameIdentifier(nameId, NameIdentifierFormats.Persistent),
        };
        response.CreateSecurityToken(audience, subjectConfirmationLifetime: 5, issuedTokenLifetime: 60);
        var binding = new Saml2PostBinding();
        binding.Bind(response);
        return Regex.Match(binding.PostContent, "name=\"SAMLResponse\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    public static Saml2AuthnResponse Read(Saml2Configuration sp, string samlResponse)
    {
        var request = new ITfoxtec.Identity.Saml2.Http.HttpRequest { Method = "POST", Form = new NameValueCollection { ["SAMLResponse"] = samlResponse } };
        var response = new Saml2AuthnResponse(sp);
        var binding = new Saml2PostBinding();
        binding.ReadSamlResponse(request, response);
        binding.Unbind(request, response);
        return response;
    }
}

public class SamlSpikeTests
{
    [Fact]
    public void ASignedResponse_IsAccepted_WithClaimsNameIdAndAssertionId()
    {
        var idpCert = SamlSpikeKit.NewCertificate("idp");
        var response = SamlSpikeKit.Response(SamlSpikeKit.IdpConfig(idpCert), "https://sp.test/saml/acs", "id_req1");
        var read = SamlSpikeKit.Read(SamlSpikeKit.SpConfig(idpCert), response);
        read.Status.ShouldBe(Saml2StatusCodes.Success);
        read.NameId.Value.ShouldBe("user-1");
        read.ClaimsIdentity.Claims.ShouldContain(c => c.Value == "sara@example.org");
        read.Saml2SecurityToken.Id.ShouldNotBeNullOrEmpty();   // the assertion id: what a replay cache keeps
    }

    [Fact]
    public void AResponseSignedWithAnotherKey_IsRefused()
    {
        var attacker = SamlSpikeKit.NewCertificate("attacker");
        var trusted = SamlSpikeKit.NewCertificate("idp");
        var response = SamlSpikeKit.Response(SamlSpikeKit.IdpConfig(attacker), "https://sp.test/saml/acs", "id_req1");
        Should.Throw<Exception>(() => SamlSpikeKit.Read(SamlSpikeKit.SpConfig(trusted), response));
    }

    [Fact]
    public void AWrongAudience_IsRefused()
    {
        var idpCert = SamlSpikeKit.NewCertificate("idp");
        var response = SamlSpikeKit.Response(SamlSpikeKit.IdpConfig(idpCert), "https://sp.test/saml/acs", "id_req1", audience: "https://other-sp.test/entity");
        Should.Throw<Exception>(() => SamlSpikeKit.Read(SamlSpikeKit.SpConfig(idpCert), response));
    }

    [Fact]
    public void TwoTrustedCertificates_BothWork_ForRotation()
    {
        var old = SamlSpikeKit.NewCertificate("idp-old");
        var fresh = SamlSpikeKit.NewCertificate("idp-new");
        var sp = SamlSpikeKit.SpConfig(old, fresh);
        SamlSpikeKit.Read(sp, SamlSpikeKit.Response(SamlSpikeKit.IdpConfig(old), "https://sp.test/saml/acs", "id1")).Status.ShouldBe(Saml2StatusCodes.Success);
        SamlSpikeKit.Read(sp, SamlSpikeKit.Response(SamlSpikeKit.IdpConfig(fresh), "https://sp.test/saml/acs", "id2")).Status.ShouldBe(Saml2StatusCodes.Success);
    }
}
