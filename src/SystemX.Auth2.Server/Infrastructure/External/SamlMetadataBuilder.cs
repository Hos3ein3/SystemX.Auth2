using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>Builds the SAML service provider metadata of one provider registration.</summary>
public sealed class SamlMetadataBuilder(IExternalSecretProtector secrets)
{
    /// <summary>Returns the metadata XML. It is signed when the provider has an SP certificate.</summary>
    public string Build(RegisteredProvider provider, string issuer)
    {
        var s = (SamlProviderSettings)provider.Settings;
        var entityId = SamlIdentitySource.SpEntityId(s, provider, issuer);
        var pfx = secrets.Unprotect(s.SpSigningCertificateProtected);
        var cert = pfx is null ? null : SamlSpCertificates.Load(pfx);
        var config = new Saml2Configuration { Issuer = entityId, SigningCertificate = cert };
        var root = issuer.TrimEnd('/');

        var sp = new SPSsoDescriptor
        {
            AuthnRequestsSigned = s.SignAuthnRequests,
            WantAssertionsSigned = s.WantAssertionsSigned,
            AssertionConsumerServices = [new AssertionConsumerService { Binding = ProtocolBindings.HttpPost, Location = new Uri(root + ExternalProviderUrls.SamlAcsPath(provider.ProjectSlug, provider.Name)) }],
            NameIDFormats = [new Uri("urn:oasis:names:tc:SAML:2.0:nameid-format:persistent"), new Uri("urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress")],
        };
        if (s.AllowSingleLogout)
            sp.SingleLogoutServices = [new SingleLogoutService { Binding = ProtocolBindings.HttpRedirect, Location = new Uri(root + ExternalProviderUrls.SamlSloPath(provider.ProjectSlug, provider.Name)) }];
        if (cert is not null)
        {
            sp.SigningCertificates = [cert];
            if (s.WantAssertionsEncrypted) sp.EncryptionCertificates = [cert];
        }
        var entity = new EntityDescriptor(config, signMetadata: cert is not null) { ValidUntil = 365, SPSsoDescriptor = sp };
        return new Saml2Metadata(entity).CreateMetadata().ToXml();
    }
}
