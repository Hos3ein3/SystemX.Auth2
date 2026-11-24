using ITfoxtec.Identity.Saml2;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.IntegrationTests;

public abstract class SamlTestBase : ExternalLoginTestBase
{
    protected readonly FakeSamlIdp Idp = new();
    protected const string Acs = "https://auth.test/saml/appa/corp-saml/acs";
    protected const string SpId = "https://auth.test/saml/appa/corp-saml";

    protected override void Customize(IServiceCollection services)
    {
        services.AddHttpClient(ExternalHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => Idp);
        services.Configure<Auth2ServerOptions>(o => o.Issuer = "https://auth.test");
    }

    protected static SamlProviderSettings Saml() => new()
    { Name = "corp-saml", Type = ExternalProviderType.Saml, Enabled = true, IdpMetadataUrl = FakeSamlIdp.MetadataUrl, DisplayName = "Company SSO", GroupsAttribute = "groups", TrustProviderEmailVerification = true };

    protected async Task PrepareSamlAsync(Action<SamlProviderSettings>? change = null, Action<ApplicationSettings>? app = null)
    {
        await SetUpAsync();
        var p = Saml();
        change?.Invoke(p);
        await UpdateSettingsAsync(A, s =>
        {
            s.Redirects.AllowedRedirectUris.Add(ClientRedirect);
            s.Methods.AllowedLoginMethods.Add(LoginMethod.External);
            s.ExternalProviders.Add(p);
            app?.Invoke(s);
        });
    }

    protected SamlIdentitySource SamlSource => Factory.Services.GetRequiredService<SamlIdentitySource>();
    protected RegisteredProvider Provider(string name = "corp-saml") => Factory.Services.GetRequiredService<IExternalProviderRegistry>().Get(A.Id, name)!;

    protected async Task<(Browser Browser, string RequestId, string Relay)> StartSamlAsync(string provider = "corp-saml")
    {
        var b = new Browser(Factory);
        var start = await StartAsync(b, provider);
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        start.Headers.Location!.Host.ShouldBe("idp.test");
        var (_, id, relay) = FakeSamlIdp.ReadRequest(start.Headers.Location);
        return (b, id, relay);
    }

    protected Task<HttpResponseMessage> PostAcsAsync(Browser b, string samlResponse, string? relay, string slug = "appa", string name = "corp-saml") =>
        b.PostFormAsync($"/saml/{slug}/{name}/acs", new Dictionary<string, string> { ["SAMLResponse"] = samlResponse, ["RelayState"] = relay ?? "" });

    /// <summary>A complete, correct sign-in. Returns the response of the ACS.</summary>
    protected async Task<HttpResponseMessage> SignInViaSamlAsync(Action<SamlResponseOptions>? change = null, Browser? existing = null)
    {
        var (b, id, relay) = await StartSamlAsync();
        var o = new SamlResponseOptions { InResponseTo = id };
        change?.Invoke(o);
        return await PostAcsAsync(existing ?? b, Idp.Response(Acs, SpId, o), relay);
    }

    protected async Task ShouldBeRefusedAsync(Func<Task<HttpResponseMessage>> action, string? reason = null)
    {
        var before = (await EventsAsync("saml.assertion.rejected")).Count;
        var r = await action();
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var events = await EventsAsync("saml.assertion.rejected");
        events.Count.ShouldBe(before + 1);
        if (reason is not null) events[^1].DataJson!.ShouldContain(reason);
    }
}

public class SamlSignInTests : SamlTestBase
{
    [Fact]
    public async Task ASignedResponse_SignsTheUserIn_RegistersThem_AndMapsAttributes()
    {
        await PrepareSamlAsync();
        var (b, id, relay) = await StartSamlAsync();
        var r = await PostAcsAsync(b, Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id, NameId = "persistent-77" }), relay);
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location().ShouldBe(ClientRedirect);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();
        var user = (await FindUserAsync("sara@example.org"))!;
        user.FirstName.ShouldBe("Sara");
        user.EmailConfirmed.ShouldBeTrue();
        (await EventsAsync("external.register")).Count.ShouldBe(1);
        await using var scope = ScopeFor("appa");
        (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>().FindByLoginAsync("corp-saml", "persistent-77")).ShouldNotBeNull();
    }

    [Fact]
    public async Task TheRequest_HasTheRightShape()
    {
        await PrepareSamlAsync(p => { p.ForceAuthn = true; p.NameIdFormat = SamlNameIdFormat.EmailAddress; });
        var b = new Browser(Factory);
        var start = await StartAsync(b, "corp-saml");
        var (xml, id, relay) = FakeSamlIdp.ReadRequest(start.Headers.Location!);
        var root = xml.DocumentElement!;
        root.LocalName.ShouldBe("AuthnRequest");
        id.ShouldStartWith("_");
        root.GetAttribute("Destination").ShouldBe(FakeSamlIdp.SsoUrl);
        root.GetAttribute("AssertionConsumerServiceURL").ShouldBe(Acs);
        root.GetAttribute("ForceAuthn").ToLowerInvariant().ShouldBe("true");
        xml.OuterXml.ShouldContain(SpId);   // the issuer is the entity id of this SP
        xml.OuterXml.ShouldContain("emailAddress");
        relay.Length.ShouldBeGreaterThan(10);
    }

    [Fact]
    public async Task TheSameResponse_CannotBeUsedTwice()
    {
        await PrepareSamlAsync();
        var (b, id, relay) = await StartSamlAsync();
        var response = Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id });
        (await PostAcsAsync(b, response, relay)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        // the state cookie was removed; a replay with a copy of it still fails on the request id and the assertion id
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, response, relay));
    }

    [Fact]
    public async Task ReplayWithTheCookieKept_IsRefusedAsAReplay()
    {
        await PrepareSamlAsync();
        var (b, id, relay) = await StartSamlAsync();
        var response = Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id });
        await PostAcsAsync(b, response, relay);
        // simulate an attacker that kept the cookie
        await using var scope = NewScope();
        var source = scope.ServiceProvider.GetRequiredService<SamlIdentitySource>();
        var challenge = await source.ChallengeAsync(new ExternalChallengeContext(Provider(), "https://auth.test", "/", null), default);
        var (_, id2, _) = FakeSamlIdp.ReadRequest(challenge.RedirectUri);
        var r2 = Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id2 });
        var ctx = new ExternalCallbackContext(Provider(), "https://auth.test", new Dictionary<string, string> { ["SAMLResponse"] = r2 }, challenge.ProtectedState);
        await source.AuthenticateCallbackAsync(ctx, default);
        var ex = await Should.ThrowAsync<ExternalLoginException>(() => source.AuthenticateCallbackAsync(ctx, default));
        ex.ErrorCode.ShouldBe(Auth2ErrorCodes.SamlReplayDetected);
    }

    [Fact]
    public async Task NothingSigned_IsRefused() =>
        await PrepareThenRefuseAsync(o => o.StripSignatures = true);

    [Fact]
    public async Task UnsignedAssertion_InsideASignedResponse_IsRefused_WhenAssertionsMustBeSigned() =>
        await PrepareThenRefuseAsync(o => { o.SignAssertion = false; o.SignResponse = true; });

    [Fact]
    public async Task ResponseSignedWithAnotherCertificate_IsRefused() =>
        await PrepareThenRefuseAsync(o => o.SignWith = FakeSamlIdp.Cert("attacker"));

    [Fact]
    public async Task WrongAudience_WrongIssuer_WrongDestination_AreRefused()
    {
        await PrepareSamlAsync();
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(o => o.Audience = "https://other-sp.test/entity"));
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(o => o.Issuer = "https://evil-idp.test/entity"));
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(o => o.Destination = "https://auth.test/saml/appa/other/acs"));
        (await FindUserAsync("sara@example.org")).ShouldBeNull();
    }

    [Fact]
    public async Task ExpiredAssertion_IsRefused()
    {
        await PrepareSamlAsync();
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(o => { o.IssuedTokenLifetimeMinutes = -10; o.ConfirmationLifetimeMinutes = -10; }));
    }

    [Fact]
    public async Task WrongOrMissingInResponseTo_IsRefused_WithoutIdpInitiated()
    {
        await PrepareSamlAsync();
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(o => o.InResponseTo = "_somebody-elses-request"), "in_response_to_mismatch");
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(o => o.InResponseTo = null), "saml_in_response_to_missing");
    }

    [Fact]
    public async Task ResponseWithoutTheStateCookie_IsRefused()
    {
        await PrepareSamlAsync();
        var (_, id, relay) = await StartSamlAsync();
        await ShouldBeRefusedAsync(() => PostAcsAsync(new Browser(Factory), Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id }), relay), "saml_state_missing");
    }

    [Fact]
    public async Task ARequestOfOneProvider_CannotBeAnsweredAtAnotherProvidersAcs()
    {
        await PrepareSamlAsync();
        var second = Saml(); second.Name = "other-saml";
        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(second));
        var (b, id, relay) = await StartSamlAsync();
        var resp = Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id });
        var r = await PostAcsAsync(b, resp, relay, name: "other-saml");
        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task PrepareThenRefuseAsync(Action<SamlResponseOptions> change)
    {
        await PrepareSamlAsync();
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(change));
        (await FindUserAsync("sara@example.org")).ShouldBeNull();
    }
}

public class SamlAttackTests : SamlTestBase
{
    [Fact]
    public async Task XmlWithADoctype_IsRefused_BeforeAnythingIsRead()
    {
        await PrepareSamlAsync();
        var (b, _, relay) = await StartSamlAsync();
        var xxe = "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><samlp:Response xmlns:samlp=\"urn:oasis:names:tc:SAML:2.0:protocol\">&x;</samlp:Response>";
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, FakeSamlIdp.Encode(xxe), relay), "saml_xml_invalid");
    }

    [Fact]
    public async Task BillionLaughs_IsRefused()
    {
        await PrepareSamlAsync();
        var (b, _, relay) = await StartSamlAsync();
        var bomb = "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;\"><!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;\">]><lolz>&lol3;</lolz>";
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, FakeSamlIdp.Encode(bomb), relay), "saml_xml_invalid");
    }

    [Fact]
    public async Task HugeMessages_AreRefused()
    {
        await PrepareSamlAsync();
        var (b, _, relay) = await StartSamlAsync();
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, new string('A', 900_000), relay), "saml_too_large");
    }

    [Fact]
    public async Task NotBase64_NotXml_AndNotAResponse_AreRefused()
    {
        await PrepareSamlAsync();
        var (b, _, relay) = await StartSamlAsync();
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, "%%%not-base64%%%", relay), "saml_xml_invalid");
        var (b2, _, relay2) = await StartSamlAsync();
        await ShouldBeRefusedAsync(() => PostAcsAsync(b2, FakeSamlIdp.Encode("<hello/>"), relay2), "saml_not_a_response");
        var (b3, _, relay3) = await StartSamlAsync();
        await ShouldBeRefusedAsync(() => PostAcsAsync(b3, "", relay3), "saml_response_missing");
    }

    [Fact]
    public async Task SignatureWrapping_ASecondForgedAssertion_IsRefused()
    {
        await PrepareSamlAsync();
        var (b, id, relay) = await StartSamlAsync();
        var valid = FakeSamlIdp.Decode(Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id, NameId = "victim" }));
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(valid);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        var original = doc.SelectSingleNode("//saml:Assertion", ns)!;
        var forged = (XmlElement)original.CloneNode(true);
        forged.SelectSingleNode("saml:Subject/saml:NameID", ns)!.InnerText = "admin";
        forged.RemoveChild(forged.SelectSingleNode("ds:Signature", ns)!);
        forged.SetAttribute("ID", "_forged");
        original.ParentNode!.InsertBefore(forged, original);   // the forged one first, the signed one stays valid
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, FakeSamlIdp.Encode(doc.OuterXml), relay), "saml_assertion_count");
        (await FindUserAsync("sara@example.org")).ShouldBeNull();
    }

    [Fact]
    public async Task SignatureWrapping_TheSignedAssertionHiddenInsideAnother_IsRefused()
    {
        await PrepareSamlAsync();
        var (b, id, relay) = await StartSamlAsync();
        var valid = FakeSamlIdp.Decode(Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id, NameId = "victim" }));
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(valid);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
        var signed = (XmlElement)doc.SelectSingleNode("//saml:Assertion", ns)!;
        // an unsigned outer assertion that carries the signed one inside an Advice element
        var outer = doc.CreateElement("saml", "Assertion", "urn:oasis:names:tc:SAML:2.0:assertion");
        outer.SetAttribute("ID", "_outer");
        var advice = doc.CreateElement("saml", "Advice", "urn:oasis:names:tc:SAML:2.0:assertion");
        signed.ParentNode!.ReplaceChild(outer, signed);
        outer.AppendChild(advice);
        advice.AppendChild(signed);
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, FakeSamlIdp.Encode(doc.OuterXml), relay));
        (await FindUserAsync("sara@example.org")).ShouldBeNull();
    }

    [Fact]
    public async Task DuplicateIds_AreRefused()
    {
        await PrepareSamlAsync();
        var (b, id, relay) = await StartSamlAsync();
        var valid = FakeSamlIdp.Decode(Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id }));
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(valid);
        var extra = doc.CreateElement("Extra");
        extra.SetAttribute("ID", doc.DocumentElement!.GetAttribute("ID"));
        doc.DocumentElement.AppendChild(extra);
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, FakeSamlIdp.Encode(doc.OuterXml), relay), "saml_duplicate_id");
    }

    [Fact]
    public async Task ChangingTheNameId_AfterSigning_BreaksTheSignature()
    {
        await PrepareSamlAsync();
        var (b, id, relay) = await StartSamlAsync();
        var valid = FakeSamlIdp.Decode(Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id, NameId = "victim" }));
        await ShouldBeRefusedAsync(() => PostAcsAsync(b, FakeSamlIdp.Encode(valid.Replace(">victim<", ">admin<")), relay));
    }
}

public class SamlOptionsTests : SamlTestBase
{
    [Fact]
    public async Task WantResponseSigned_NeedsASignatureOnTheWholeResponse()
    {
        await PrepareSamlAsync(p => p.WantResponseSigned = true);
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(o => o.SignResponse = false), "saml_response_not_signed");
        (await SignInViaSamlAsync(o => o.SignResponse = true)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task WantAssertionsEncrypted_RefusesPlainAssertions_AndAcceptsEncryptedOnes()
    {
        var spCert = SamlSpCertificates.Generate("sp", DateTimeOffset.UtcNow);
        await PrepareSamlAsync();
        var secrets = Factory.Services.GetRequiredService<IExternalSecretProtector>();
        await UpdateSettingsAsync(A, s =>
        {
            var p = (SamlProviderSettings)s.ExternalProviders[0];
            p.WantAssertionsEncrypted = true;
            p.SpSigningCertificateProtected = secrets.Protect(spCert.PfxBase64);
        });
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(), "saml_assertion_not_encrypted");
        var sp = SamlSpCertificates.Load(spCert.PfxBase64);
        var ok = await SignInViaSamlAsync(o => o.EncryptTo = X509CertificateLoader.LoadCertificate(sp.RawData));
        ok.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task TwoIdpCertificates_BothWork_ForRotation()
    {
        var next = FakeSamlIdp.Cert("idp-next");
        await PrepareSamlAsync();
        await Factory.Services.GetRequiredService<ISamlProviderMaterial>().GetAsync(Provider(), default);   // the metadata is read and cached now
        Idp.PublishedCertificates.Add(next);
        // the cached metadata still has one certificate: the new one is refused until the metadata is refreshed
        await ShouldBeRefusedAsync(() => SignInViaSamlAsync(o => o.SignWith = next));
        var manual = Saml(); manual.Name = "manual-saml"; manual.IdpMetadataUrl = null;
        manual.IdpEntityId = FakeSamlIdp.EntityId; manual.IdpSsoUrl = FakeSamlIdp.SsoUrl;
        manual.IdpSigningCertificates = [Convert.ToBase64String(Idp.Signing.RawData), Convert.ToBase64String(next.RawData)];
        await UpdateSettingsAsync(A, s => s.ExternalProviders.Add(manual));
        var (b, id, relay) = await StartSamlAsync("manual-saml");
        var viaNew = Idp.Response("https://auth.test/saml/appa/manual-saml/acs", "https://auth.test/saml/appa/manual-saml", new SamlResponseOptions { InResponseTo = id, SignWith = next });
        (await PostAcsAsync(b, viaNew, relay, name: "manual-saml")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var (b2, id2, relay2) = await StartSamlAsync("manual-saml");
        var viaOld = Idp.Response("https://auth.test/saml/appa/manual-saml/acs", "https://auth.test/saml/appa/manual-saml", new SamlResponseOptions { InResponseTo = id2, NameId = "second" });
        (await PostAcsAsync(b2, viaOld, relay2, name: "manual-saml")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task IdpInitiated_IsOffByDefault_AndNeedsASignedResponseWhenOn()
    {
        await PrepareSamlAsync();
        var unsolicited = Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = null, SignResponse = true });
        await ShouldBeRefusedAsync(() => PostAcsAsync(new Browser(Factory), unsolicited, null), "saml_state_missing");

        await UpdateSettingsAsync(A, s => ((SamlProviderSettings)s.ExternalProviders[0]).AllowIdpInitiated = true);
        var b = new Browser(Factory);
        var r = await PostAcsAsync(b, unsolicited, "https://evil.test/steal");
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldNotContain("evil.test");   // RelayState is never trusted
        (await EventsAsync("saml.idp_initiated")).Count.ShouldBe(1);

        await ShouldBeRefusedAsync(() => PostAcsAsync(new Browser(Factory), Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = null, SignResponse = false, NameId = "other" }), null), "saml_idp_initiated_needs_signed_response");
    }

    [Fact]
    public async Task SubjectAttribute_AndAttributeMappings_ChooseTheIdAndTheFields()
    {
        await PrepareSamlAsync(p => { p.SubjectAttribute = "employeeid"; p.AttributeMappings["mailaddress"] = "email"; });
        var r = await SignInViaSamlAsync(o =>
        {
            o.Attributes = new() { ["employeeid"] = ["E-100"], ["mailaddress"] = ["mapped@example.org"], ["groups"] = ["a"] };
        });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await FindUserAsync("mapped@example.org")).ShouldNotBeNull();
        await using var scope = ScopeFor("appa");
        (await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>().FindByLoginAsync("corp-saml", "E-100")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Groups_FromTheGroupsAttribute_AreMapped()
    {
        await PrepareSamlAsync(p => p.GroupMappingMode = GroupMappingMode.Sync);
        Guid role;
        await using (var scope = ScopeFor("appa"))
        {
            await scope.ServiceProvider.GetRequiredService<IAccessService>().CreateRoleAsync("accountant");
            role = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                scope.ServiceProvider.GetRequiredService<Persistence.Auth2DbContext>().Roles.Where(r => r.Name == "accountant").Select(r => r.Id));
        }
        await using (var scope = NewScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var db = scope.ServiceProvider.GetRequiredService<Persistence.Auth2DbContext>();
            db.ExternalGroupMappings.Add(new ExternalGroupMapping { ApplicationId = A.Id, ProviderName = "corp-saml", SourceType = MappingSourceType.Group, MatchType = MappingMatchType.Exact, SourceValue = "finance", TargetType = MappingTargetType.Role, TargetId = role });
            await db.SaveChangesAsync();
        }
        await SignInViaSamlAsync();
        await using var check = ScopeFor("appa");
        var users = check.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        (await users.GetRolesAsync((await users.FindByEmailAsync("sara@example.org"))!)).ShouldContain("accountant");
    }

    [Fact]
    public async Task PostBinding_GivesAFormForTheUi_AndSignedRequestsNeedACertificate()
    {
        await PrepareSamlAsync(p => p.AuthnRequestBinding = SamlBinding.Post);
        var b = new Browser(Factory);
        var r = await StartAsync(b, "corp-saml");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);   // the page model exposes PostAction and PostFields for the UI
        var challenge = await SamlSource.ChallengeAsync(new ExternalChallengeContext(Provider(), "https://auth.test", "/", null), default);
        challenge.PostFields!["SAMLRequest"].ShouldNotBeNullOrEmpty();
        challenge.RedirectUri.ToString().ShouldBe(FakeSamlIdp.SsoUrl);

        await UpdateSettingsAsync(A, s => ((SamlProviderSettings)s.ExternalProviders[0]).AuthnRequestBinding = SamlBinding.Redirect);
        var spCert = SamlSpCertificates.Generate("sp", DateTimeOffset.UtcNow);
        var secrets = Factory.Services.GetRequiredService<IExternalSecretProtector>();
        await UpdateSettingsAsync(A, s =>
        {
            var p = (SamlProviderSettings)s.ExternalProviders[0];
            p.SignAuthnRequests = true;
            p.SpSigningCertificateProtected = secrets.Protect(spCert.PfxBase64);
        });
        var signed = await SamlSource.ChallengeAsync(new ExternalChallengeContext(Provider(), "https://auth.test", "/", null), default);
        signed.RedirectUri.Query.ShouldContain("Signature=");
        signed.RedirectUri.Query.ShouldContain("SigAlg=");
    }
}

public class SamlMetadataTests : SamlTestBase
{
    [Fact]
    public async Task SpMetadata_HasTheEntityIdAcsAndNoSecrets()
    {
        var spCert = SamlSpCertificates.Generate("sp", DateTimeOffset.UtcNow);
        await PrepareSamlAsync();
        var secrets = Factory.Services.GetRequiredService<IExternalSecretProtector>();
        await UpdateSettingsAsync(A, s => ((SamlProviderSettings)s.ExternalProviders[0]).SpSigningCertificateProtected = secrets.Protect(spCert.PfxBase64));
        var r = await new Browser(Factory).GetAsync("/saml/appa/corp-saml/metadata");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.ShouldBe("application/samlmetadata+xml");
        var xml = await r.Content.ReadAsStringAsync();
        xml.ShouldContain("entityID=\"http://localhost/saml/appa/corp-saml\"".Replace("http://localhost", "https://auth.test"));
        xml.ShouldContain("https://auth.test/saml/appa/corp-saml/acs");
        xml.ShouldContain("X509Certificate");
        xml.ShouldNotContain("PrivateKey", Case.Insensitive);
        xml.ShouldNotContain(spCert.PfxBase64[..40]);
        (await new Browser(Factory).GetAsync("/saml/appa/nope/metadata")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await new Browser(Factory).GetAsync("/saml/appb/corp-saml/metadata")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IdpMetadata_IsCached_AndTheLastGoodCopyIsUsedWhenARefreshFails()
    {
        await PrepareSamlAsync();
        var material = Factory.Services.GetRequiredService<ISamlProviderMaterial>();
        var first = await material.GetAsync(Provider(), default);
        first.EntityId.ShouldBe(FakeSamlIdp.EntityId);
        first.SsoUrl.ToString().ShouldBe(FakeSamlIdp.SsoUrl);
        first.SloUrl!.ToString().ShouldBe("https://idp.test/slo");
        await material.GetAsync(Provider(), default);
        Idp.MetadataRequests.ShouldBe(1);   // the second call came from the cache
        Idp.MetadataDown = true;
        (await material.GetAsync(Provider(), default)).EntityId.ShouldBe(FakeSamlIdp.EntityId);
    }

    [Fact]
    public async Task IdpMetadata_WithADoctype_OrNoCertificate_IsRefused()
    {
        Should.Throw<InvalidOperationException>(() => SamlXml.ParseIdpMetadata("<!DOCTYPE x [<!ENTITY y SYSTEM \"file:///etc/passwd\">]><EntityDescriptor xmlns=\"urn:oasis:names:tc:SAML:2.0:metadata\" entityID=\"a\">&y;</EntityDescriptor>"));
        Should.Throw<InvalidOperationException>(() => SamlXml.ParseIdpMetadata("<EntityDescriptor xmlns=\"urn:oasis:names:tc:SAML:2.0:metadata\" entityID=\"a\"><IDPSSODescriptor><SingleSignOnService Binding=\"urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect\" Location=\"https://x/sso\"/></IDPSSODescriptor></EntityDescriptor>"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExpiringCertificates_AreFound()
    {
        await PrepareSamlAsync();
        var material = await Factory.Services.GetRequiredService<ISamlProviderMaterial>().GetAsync(Provider(), default);
        material.ExpiringWithin(TimeSpan.FromDays(30), DateTimeOffset.UtcNow).ShouldBeEmpty();
        material.ExpiringWithin(TimeSpan.FromDays(400), DateTimeOffset.UtcNow).Count().ShouldBe(1);
    }

    [Fact]
    public async Task SpCertificate_RoundTrips_AndIsEncryptedInSettings()
    {
        var (pfx, notAfter) = SamlSpCertificates.Generate("sp", DateTimeOffset.UtcNow, 3);
        notAfter.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddYears(2));
        SamlSpCertificates.Load(pfx).HasPrivateKey.ShouldBeTrue();
        await Task.CompletedTask;
    }
}

public class SamlLogoutTests : SamlTestBase
{
    private static string SignedLogoutQuery(FakeSamlIdp idp, string nameId, string destination, System.Security.Cryptography.X509Certificates.X509Certificate2? cert = null)
    {
        var config = new ITfoxtec.Identity.Saml2.Saml2Configuration { Issuer = FakeSamlIdp.EntityId, SigningCertificate = cert ?? idp.Signing, SignatureAlgorithm = ITfoxtec.Identity.Saml2.Schemas.Saml2SecurityAlgorithms.RsaSha256Signature };
        var request = new ITfoxtec.Identity.Saml2.Saml2LogoutRequest(config)
        {
            Destination = new Uri(destination),
            NameId = new Microsoft.IdentityModel.Tokens.Saml2.Saml2NameIdentifier(nameId),
        };
        var binding = new ITfoxtec.Identity.Saml2.Saml2RedirectBinding();
        binding.Bind(request);
        return binding.RedirectLocation.Query;
    }

    [Fact]
    public async Task SignedLogoutRequest_SignsTheUserOut_AndAnswersTheProvider()
    {
        var spCert = SamlSpCertificates.Generate("sp", DateTimeOffset.UtcNow);
        await PrepareSamlAsync(p => p.AllowSingleLogout = true);
        var secrets = Factory.Services.GetRequiredService<IExternalSecretProtector>();
        await UpdateSettingsAsync(A, s => ((SamlProviderSettings)s.ExternalProviders[0]).SpSigningCertificateProtected = secrets.Protect(spCert.PfxBase64));

        var (b, id, relay) = await StartSamlAsync();
        (await PostAcsAsync(b, Idp.Response(Acs, SpId, new SamlResponseOptions { InResponseTo = id, NameId = "slo-user" }), relay)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeTrue();

        var r = await b.GetAsync("/saml/appa/corp-saml/slo" + SignedLogoutQuery(Idp, "slo-user", "https://auth.test/saml/appa/corp-saml/slo"));
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        r.Location()!.ShouldStartWith("https://idp.test/slo");
        r.Location()!.ShouldContain("SAMLResponse=");
        b.HasCookie(".SystemX.Auth2.Session").ShouldBeFalse();
        (await EventsAsync("saml.slo")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task LogoutRequest_Unsigned_WrongKey_OrWhenSloIsOff_IsRefused()
    {
        await PrepareSamlAsync();   // AllowSingleLogout is off by default
        var q = SignedLogoutQuery(Idp, "x", "https://auth.test/saml/appa/corp-saml/slo");
        (await new Browser(Factory).GetAsync("/saml/appa/corp-saml/slo" + q)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await UpdateSettingsAsync(A, s => ((SamlProviderSettings)s.ExternalProviders[0]).AllowSingleLogout = true);
        var unsigned = q[..q.IndexOf("&SigAlg", StringComparison.Ordinal)];
        (await new Browser(Factory).GetAsync("/saml/appa/corp-saml/slo" + unsigned)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var forged = SignedLogoutQuery(Idp, "x", "https://auth.test/saml/appa/corp-saml/slo", FakeSamlIdp.Cert("attacker"));
        (await new Browser(Factory).GetAsync("/saml/appa/corp-saml/slo" + forged)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
