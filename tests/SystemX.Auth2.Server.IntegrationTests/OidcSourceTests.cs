using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.IntegrationTests;

public abstract class OidcTestBase : IdentityTestBase
{
    protected readonly FakeOidcProvider Fake = new();
    protected const string Issuer = "https://auth.test";

    protected override void Customize(IServiceCollection services)
    {
        services.AddHttpClient(ExternalHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => Fake);   // the extra GitHub calls of the Server
        FakeHttpShortcut.Install(services, Fake);                                                       // everything OpenIddict.Client sends
    }

    protected OpenIddictIdentitySource Source => Factory.Services.GetRequiredService<OpenIddictIdentitySource>();
    protected IExternalSecretProtector Secrets => Factory.Services.GetRequiredService<IExternalSecretProtector>();

    protected async Task<RegisteredProvider> AddAsync(Project app, OidcProviderSettings settings)
    {
        settings.Enabled = true;
        if (settings.ClientSecretProtected is null && settings.Type != ExternalProviderType.Apple) settings.ClientSecretProtected = Secrets.Protect("client-secret");
        await UpdateSettingsAsync(app, s => s.ExternalProviders.Add(settings));
        return Factory.Services.GetRequiredService<IExternalProviderRegistry>().Get(app.Id, settings.Name)!;
    }

    /// <summary>Runs a whole sign-in against the fake provider and returns the identity.</summary>
    protected async Task<ExternalCallbackResult> SignInAsync(RegisteredProvider p, FakeUser user, string returnUrl = "/home", Action<Dictionary<string, string>>? change = null)
    {
        var challenge = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, returnUrl, null), default);
        var code = Fake.Authorize(challenge.RedirectUri, user);
        var parameters = new Dictionary<string, string> { ["code"] = code, ["state"] = Fake.Parse(challenge.RedirectUri).State };
        // Google and GitHub answer with the RFC 9207 "iss" parameter, and OpenIddict requires it for the providers that are known to send it.
        if (p.Type == ExternalProviderType.Google) parameters["iss"] = "https://accounts.google.com";
        if (p.Type == ExternalProviderType.GitHub) parameters["iss"] = "https://github.com/login/oauth";
        change?.Invoke(parameters);
        return await Source.AuthenticateCallbackAsync(new ExternalCallbackContext(p, Issuer, parameters, challenge.ProtectedState), default);
    }

    protected static async Task<ExternalLoginException> ShouldFailAsync(Func<Task> action, string reason)
    {
        var ex = await Should.ThrowAsync<ExternalLoginException>(action);
        ex.ErrorCode.ShouldBe(Auth2ErrorCodes.ExternalClaimsInvalid);
        ex.Reason.ShouldBe(reason, "OpenIddict said: " + ex.Data["openiddict"] + " claims: " + ex.Data["claimTypes"]);
        return ex;
    }
}

public class OidcFlowTests : OidcTestBase
{
    private static OidcProviderSettings Generic() => new()
    { Name = "corp", Type = ExternalProviderType.GenericOidc, ClientId = "client-1", Authority = "https://idp.corp.test/realm" };

    [Fact]
    public async Task Challenge_UsesCodeFlow_WithPkceStateNonce_AndTheProviderCallbackUrl()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic());
        var c = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/home", "sara@corp.test"), default);
        var r = Fake.Parse(c.RedirectUri);
        c.RedirectUri.Host.ShouldBe("idp.corp.test");
        r.ClientId.ShouldBe("client-1");
        r.RedirectUri.ShouldBe("https://auth.test/callback/login/appa/corp");
        r.Scope.ShouldBe("openid email profile");
        r.State.Length.ShouldBeGreaterThan(30);
        r.Nonce!.Length.ShouldBeGreaterThan(30);
        c.RedirectUri.Query.ShouldContain("login_hint=sara@corp.test");
        c.ProtectedState.ShouldNotContain(r.State);   // the state is encrypted, not readable
    }

    [Fact]
    public async Task FullSignIn_ReturnsTheIdentity_AndTheReturnUrl()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic());
        var result = await SignInAsync(p, new FakeUser { Sub = "abc", Email = "sara@corp.test", Groups = ["staff", "finance"] }, "/orders");
        result.ReturnUrl.ShouldBe("/orders");
        result.Identity.Subject.ShouldBe("abc");
        result.Identity.Provider.ShouldBe("corp");
        result.Identity.Issuer.ShouldBe("https://idp.corp.test/realm");
        result.Identity.Email.ShouldBe("sara@corp.test");
        result.Identity.EmailVerified.ShouldBe(true);
        result.Identity.FirstName.ShouldBe("Sara");
        Fake.LastClientSecret.ShouldBe("client-secret");
    }

    [Fact]
    public async Task ACallbackCannotBeUsedTwice()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic());
        var challenge = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/", null), default);
        var code = Fake.Authorize(challenge.RedirectUri, new FakeUser());
        var ps = new Dictionary<string, string> { ["code"] = code, ["state"] = Fake.Parse(challenge.RedirectUri).State };
        await Source.AuthenticateCallbackAsync(new ExternalCallbackContext(p, Issuer, ps, challenge.ProtectedState), default);
        await ShouldFailAsync(() => Source.AuthenticateCallbackAsync(new ExternalCallbackContext(p, Issuer, ps, challenge.ProtectedState), default), "state_replayed");
    }

    [Fact]
    public async Task WrongState_MissingState_AndTamperedCookie_AreRefused()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic());
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser(), change: ps => ps["state"] = "other"), "state_mismatch");
        await ShouldFailAsync(() => Source.AuthenticateCallbackAsync(new ExternalCallbackContext(p, Issuer, new Dictionary<string, string> { ["code"] = "x", ["state"] = "y" }, null), default), "state_missing");
        await ShouldFailAsync(() => Source.AuthenticateCallbackAsync(new ExternalCallbackContext(p, Issuer, new Dictionary<string, string> { ["code"] = "x", ["state"] = "y" }, "garbage"), default), "state_invalid");
    }

    [Fact]
    public async Task ACallbackForAnotherProvider_IsRefused()
    {
        await SetUpAsync();
        var one = await AddAsync(A, Generic());
        var two = await AddAsync(A, new OidcProviderSettings { Name = "other", Type = ExternalProviderType.GenericOidc, ClientId = "client-2", Authority = "https://idp.other.test" });
        var challenge = await Source.ChallengeAsync(new ExternalChallengeContext(one, Issuer, "/", null), default);
        var code = Fake.Authorize(challenge.RedirectUri, new FakeUser());
        var ps = new Dictionary<string, string> { ["code"] = code, ["state"] = Fake.Parse(challenge.RedirectUri).State };
        await ShouldFailAsync(() => Source.AuthenticateCallbackAsync(new ExternalCallbackContext(two, Issuer, ps, challenge.ProtectedState), default), "wrong_provider");
    }

    [Fact]
    public async Task ACallbackFromTheSameProviderNameInAnotherApplication_IsRefused()
    {
        await SetUpAsync();
        var inA = await AddAsync(A, Generic());
        var inB = await AddAsync(B, Generic());
        var challenge = await Source.ChallengeAsync(new ExternalChallengeContext(inA, Issuer, "/", null), default);
        var code = Fake.Authorize(challenge.RedirectUri, new FakeUser());
        var ps = new Dictionary<string, string> { ["code"] = code, ["state"] = Fake.Parse(challenge.RedirectUri).State };
        await ShouldFailAsync(() => Source.AuthenticateCallbackAsync(new ExternalCallbackContext(inB, Issuer, ps, challenge.ProtectedState), default), "wrong_provider");
    }

    [Fact]
    public async Task ProviderError_AndMissingCode_AreRefused()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic());
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser(), change: ps => ps["error"] = "access_denied"), "provider_error");
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser(), change: ps => ps.Remove("code")), "code_missing");
    }

    [Fact]
    public async Task IdToken_WithBadSignature_WrongAudience_Expired_OrWrongNonce_IsRefused()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic());
        Fake.TamperSignature = true;
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser()), "id_token_invalid");
        Fake.TamperSignature = false; Fake.AudienceOverride = "someone-else";
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser()), "id_token_invalid");
        Fake.AudienceOverride = null; Fake.ExpiredToken = true;
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser()), "id_token_invalid");
        Fake.ExpiredToken = false; Fake.NonceOverride = "not-mine";
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser()), "nonce_mismatch");
    }

    [Fact]
    public async Task TokenRequestFailure_IsRefused_AndTheVerifierIsChecked()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic());
        Fake.RejectTokenRequest = true;
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser()), "token_request_failed");
        Fake.RejectTokenRequest = false;
        // A code that was issued for another challenge fails PKCE at the provider.
        var c1 = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/", null), default);
        var c2 = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/", null), default);
        var codeForOne = Fake.Authorize(c1.RedirectUri, new FakeUser());
        await ShouldFailAsync(() => Source.AuthenticateCallbackAsync(new ExternalCallbackContext(p, Issuer,
            new Dictionary<string, string> { ["code"] = codeForOne, ["state"] = Fake.Parse(c2.RedirectUri).State }, c2.ProtectedState), default), "token_request_failed");
    }

    [Fact]
    public async Task StoreProviderTokens_IsOffByDefault_AndReturnsTokensWhenOn()
    {
        await SetUpAsync();
        var off = await AddAsync(A, Generic());
        (await SignInAsync(off, new FakeUser())).Identity.Tokens.ShouldBeNull();
        var on = Generic(); on.Name = "tokens"; on.StoreProviderTokens = true;
        var p = await AddAsync(A, on);
        var t = (await SignInAsync(p, new FakeUser())).Identity.Tokens!;
        t.RefreshToken.ShouldBe("rt-1");
        t.AccessToken.ShouldStartWith("at-");
    }

    [Fact]
    public async Task ClaimMappings_AndGroupsClaim_AreRead()
    {
        await SetUpAsync();
        var s = Generic();
        s.ClaimMappings["preferred_mail"] = "email";
        s.GroupsClaim = "groups";
        var p = await AddAsync(A, s);
        var user = new FakeUser { Email = "ignored@corp.test" };
        user.Extra["preferred_mail"] = "mapped@corp.test";
        user.Groups = ["g1", "g2"];
        var id = (await SignInAsync(p, user)).Identity;
        id.Email.ShouldBe("mapped@corp.test");
        id.Groups.ShouldBe(["g1", "g2"]);
    }

    [Fact]
    public async Task Amr_WithMfa_IsReported()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Generic());
        (await SignInAsync(p, new FakeUser { Amr = ["pwd", "otp"] })).Identity.UsedMultiFactor.ShouldBe(true);
        (await SignInAsync(p, new FakeUser { Amr = ["pwd"] })).Identity.UsedMultiFactor.ShouldBe(false);
        (await SignInAsync(p, new FakeUser { Amr = null })).Identity.UsedMultiFactor.ShouldBeNull();
    }
}

public class GenericOidcTests : OidcTestBase
{
    [Fact]
    public async Task IssuerOfAnotherServer_IsRefused_UnlessItIsAllowed()
    {
        await SetUpAsync();
        var s = new OidcProviderSettings { Name = "corp", Type = ExternalProviderType.GenericOidc, ClientId = "c", Authority = "https://idp.corp.test" };
        var p = await AddAsync(A, s);
        Fake.IssuerOverride = "https://evil.test";
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser()), "discovery_failed");   // discovery says the issuer is not the authority
        Fake.IssuerOverride = null;
        (await SignInAsync(p, new FakeUser())).Identity.Subject.ShouldBe("user-1");
    }

    [Fact]
    public async Task MultiTenantIssuers_NeedAllowedIssuers()
    {
        await SetUpAsync();
        // The discovery document names the authority; the identity token names the tenant of the user (a multi-tenant provider).
        var plain = await AddAsync(A, new OidcProviderSettings { Name = "corp", Type = ExternalProviderType.GenericOidc, ClientId = "c", Authority = "https://idp.corp.test" });
        Fake.TokenIssuerOverride = "https://tenant-7.corp.test";
        await ShouldFailAsync(() => SignInAsync(plain, new FakeUser()), "issuer_invalid");
        var allowed = await AddAsync(A, new OidcProviderSettings { Name = "corp2", Type = ExternalProviderType.GenericOidc, ClientId = "c", Authority = "https://idp.corp.test", AllowedIssuers = ["https://tenant-7.corp.test"] });
        (await SignInAsync(allowed, new FakeUser())).Identity.Issuer.ShouldBe("https://tenant-7.corp.test");
        Fake.TokenIssuerOverride = "https://tenant-8.corp.test";
        await ShouldFailAsync(() => SignInAsync(allowed, new FakeUser()), "issuer_invalid");   // only the listed tenants
    }

    [Fact]
    public async Task NameAndEmailClaims_CanBeChosen()
    {
        await SetUpAsync();
        var s = new OidcProviderSettings { Name = "corp", Type = ExternalProviderType.GenericOidc, ClientId = "c", Authority = "https://idp.corp.test", EmailClaim = "mail", NameClaim = "cn" };
        var p = await AddAsync(A, s);
        var u = new FakeUser { Email = null, Name = null };
        u.Extra["mail"] = "x@corp.test"; u.Extra["cn"] = "Xavier";
        var id = (await SignInAsync(p, u)).Identity;
        id.Email.ShouldBe("x@corp.test");
        id.DisplayName.ShouldBe("Xavier");
    }
}

public class GoogleOidcTests : OidcTestBase
{
    private static OidcProviderSettings Google(string? hd = null) => new() { Name = "google", Type = ExternalProviderType.Google, ClientId = "g-client", HostedDomain = hd };

    [Fact]
    public async Task SignIn_Works_WithGoogleEndpoints_AndSendsHd()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Google("corp.com"));
        var c = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/", null), default);
        c.RedirectUri.Host.ShouldBe("accounts.google.com");
        c.RedirectUri.Query.ShouldContain("hd=corp.com");
        var code = Fake.Authorize(c.RedirectUri, new FakeUser { Hd = "corp.com" });
        var r = await Source.AuthenticateCallbackAsync(new ExternalCallbackContext(p, Issuer,
            new Dictionary<string, string> { ["code"] = code, ["state"] = Fake.Parse(c.RedirectUri).State }, c.ProtectedState), default);
        r.Identity.Issuer.ShouldBe("https://accounts.google.com");
    }

    [Fact]
    public async Task HostedDomain_OfAnotherCompany_IsRefused()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Google("corp.com"));
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser { Hd = "other.com" }), "hosted_domain_mismatch");
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser { Hd = null }), "hosted_domain_mismatch");
    }

    [Fact]
    public async Task WithoutHostedDomain_AnyAccountIsAccepted()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Google());
        (await SignInAsync(p, new FakeUser())).Identity.EmailVerified.ShouldBe(true);
    }
}

public class MicrosoftOidcTests : OidcTestBase
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";

    private OidcProviderSettings Ms(string? tenant = "common", params string[] allowed)
    {
        Fake.MicrosoftStyleIssuer = true;
        return new OidcProviderSettings { Name = "microsoft", Type = ExternalProviderType.Microsoft, ClientId = "m-client", TenantId = tenant, AllowedTenants = [.. allowed] };
    }

    [Fact]
    public async Task CommonEndpoint_AcceptsAnyTenant_AndChecksTheTenantIssuer()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Ms());
        var c = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/", null), default);
        c.RedirectUri.Host.ShouldBe("login.microsoftonline.com");
        c.RedirectUri.AbsolutePath.ShouldStartWith("/common/v2.0");
        (await SignInAsync(p, new FakeUser { Tid = TenantA })).Identity.Issuer.ShouldBe($"https://login.microsoftonline.com/{TenantA}/v2.0");
    }

    [Fact]
    public async Task AllowedTenants_AreEnforced()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Ms("organizations", TenantA));
        (await SignInAsync(p, new FakeUser { Tid = TenantA })).Identity.Subject.ShouldBe("user-1");
        // "organizations" accepts any work tenant at the library; our AllowedTenants list is what refuses the other one.
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser { Tid = TenantB }), "tenant_not_allowed");
    }

    [Fact]
    public async Task SingleTenantId_RefusesOtherTenants()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Ms(TenantA));
        // OpenIddict checks the issuer of the token against the tenant of the registration first, so a token of another tenant fails there.
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser { Tid = TenantB }), "issuer_invalid");
    }

    [Fact]
    public async Task TokenWithoutTenant_OrWithAMismatchingIssuer_IsRefused()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Ms());
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser { Tid = null }), "issuer_invalid");
        Fake.IssuerOverride = "https://login.microsoftonline.com/other/v2.0";
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser { Tid = TenantA }), "issuer_invalid");
    }
}

public class GitHubOidcTests : OidcTestBase
{
    private static OidcProviderSettings Gh() => new() { Name = "github", Type = ExternalProviderType.GitHub, ClientId = "gh-client" };

    [Fact]
    public async Task SignIn_UsesTheOAuthEndpoints_AndTheVerifiedPrimaryEmail()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Gh());
        var c = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/", null), default);
        c.RedirectUri.Host.ShouldBe("github.com");
        c.RedirectUri.Query.ShouldNotContain("nonce");
        var id = (await SignInAsync(p, new FakeUser { Sub = "gh-4711", Email = "dev@example.com" })).Identity;
        id.Subject.ShouldBe("4711");
        id.Email.ShouldBe("dev@example.com");
        id.EmailVerified.ShouldBe(true);
        id.DisplayName.ShouldBe("Sara Test");
    }

    [Fact]
    public async Task UnverifiedPrimaryEmail_IsRefused_ByDefault()
    {
        await SetUpAsync();
        var p = await AddAsync(A, Gh());
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser { Sub = "gh-1", GitHubEmailVerified = false }), "email_not_verified");
        var open = Gh(); open.Name = "open"; open.RequireVerifiedPrimaryEmail = false;
        var p2 = await AddAsync(A, open);
        (await SignInAsync(p2, new FakeUser { Sub = "gh-1", GitHubEmailVerified = false })).Identity.EmailVerified.ShouldBe(false);
    }

    [Fact]
    public async Task AllowedOrganizations_AreEnforced()
    {
        await SetUpAsync();
        var s = Gh(); s.AllowedOrganizations = ["acme"];
        var p = await AddAsync(A, s);
        (await SignInAsync(p, new FakeUser { Sub = "gh-1", GitHubOrgs = ["ACME", "x"] })).Identity.Subject.ShouldBe("1");
        await ShouldFailAsync(() => SignInAsync(p, new FakeUser { Sub = "gh-1", GitHubOrgs = ["other"] }), "organization_not_allowed");
    }
}

public class FacebookOidcTests : OidcTestBase
{
    [Fact]
    public async Task SignIn_UsesTheGraphApiVersion_AndNeverReportsEmailAsVerified()
    {
        await SetUpAsync();
        var p = await AddAsync(A, new OidcProviderSettings { Name = "facebook", Type = ExternalProviderType.Facebook, ClientId = "fb", ApiVersion = "v18.0" });
        var c = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/", null), default);
        c.RedirectUri.AbsolutePath.ShouldBe("/v18.0/dialog/oauth");
        var id = (await SignInAsync(p, new FakeUser { Sub = "fb-77" })).Identity;
        id.Subject.ShouldBe("fb-77");
        id.Email.ShouldBe("sara@example.com");
        id.EmailVerified.ShouldBeNull();
        id.FirstName.ShouldBe("Sara");
        Fake.Requests.ShouldContain("GET graph.facebook.com/v18.0/me");
    }
}

public class AppleOidcTests : OidcTestBase
{
    private static (string Pem, ECDsa Key) NewKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key);
    }

    [Fact]
    public async Task ClientSecret_IsASignedJwt_WithTeamKeyAndAudience()
    {
        await SetUpAsync();
        var (pem, key) = NewKey();
        var p = await AddAsync(A, new OidcProviderSettings
        { Name = "apple", Type = ExternalProviderType.Apple, ClientId = "com.example.web", TeamId = "TEAM123456", KeyId = "KEY1234567", PrivateKeyProtected = Secrets.Protect(pem) });
        var c = await Source.ChallengeAsync(new ExternalChallengeContext(p, Issuer, "/", null), default);
        c.RedirectUri.Host.ShouldBe("appleid.apple.com");
        c.RedirectUri.Query.ShouldContain("response_mode=form_post");
        await SignInAsync(p, new FakeUser { Sub = "apple-1" });

        var jwt = new JsonWebToken(Fake.LastClientSecret!);
        jwt.Alg.ShouldBe("ES256");
        jwt.Kid.ShouldBe("KEY1234567");
        jwt.Issuer.ShouldBe("TEAM123456");
        jwt.Subject.ShouldBe("com.example.web");
        jwt.Audiences.ShouldContain("https://appleid.apple.com");
        (jwt.ValidTo - DateTime.UtcNow).ShouldBeLessThan(TimeSpan.FromMinutes(6));
        var ok = await new JsonWebTokenHandler().ValidateTokenAsync(Fake.LastClientSecret!, new TokenValidationParameters
        { IssuerSigningKey = new ECDsaSecurityKey(key), ValidateIssuer = false, ValidateAudience = false });
        ok.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task EveryRequest_GetsAFreshSecret()
    {
        await SetUpAsync();
        var (pem, _) = NewKey();
        var p = await AddAsync(A, new OidcProviderSettings
        { Name = "apple", Type = ExternalProviderType.Apple, ClientId = "svc", TeamId = "T", KeyId = "K", PrivateKeyProtected = Secrets.Protect(pem) });
        await SignInAsync(p, new FakeUser());
        var first = Fake.LastClientSecret;
        await Task.Delay(1100);
        await SignInAsync(p, new FakeUser());
        Fake.LastClientSecret.ShouldNotBe(first);
    }

    [Fact]
    public async Task EmailFromApple_IsVerified_WhenTheTokenSaysSo()
    {
        await SetUpAsync();
        var (pem, _) = NewKey();
        var p = await AddAsync(A, new OidcProviderSettings
        { Name = "apple", Type = ExternalProviderType.Apple, ClientId = "svc", TeamId = "T", KeyId = "K", PrivateKeyProtected = Secrets.Protect(pem) });
        var id = (await SignInAsync(p, new FakeUser { Sub = "a1", EmailVerified = true })).Identity;
        id.EmailVerified.ShouldBe(true);
    }
}

public class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void IsPublic_ClassifiesAddresses(string address, bool expected) =>
        SsrfGuard.IsPublic(System.Net.IPAddress.Parse(address)).ShouldBe(expected);

    [Theory]
    [InlineData("http://idp.test/x", false)]
    [InlineData("https://user:pw@idp.test/x", false)]
    [InlineData("https://idp.test/x", true)]
    public void EnsureAllowedUrl_RequiresHttpsWithoutUserInfo(string url, bool ok)
    {
        if (ok) SsrfGuard.EnsureAllowedUrl(new Uri(url), false);
        else Should.Throw<InvalidOperationException>(() => SsrfGuard.EnsureAllowedUrl(new Uri(url), false));
    }

    [Fact]
    public void Http_IsAllowedOnlyWhenPrivateNetworksAreAllowed() => SsrfGuard.EnsureAllowedUrl(new Uri("http://localhost/x"), true);
}

public class SsrfHttpClientTests : ServerTestBase
{
    [Fact]
    public async Task TheRealClient_RefusesALoopbackAddress()
    {
        await MigrateAndSeedAsync();
        var client = Factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ExternalHttp.ClientName);
        var ex = await Should.ThrowAsync<HttpRequestException>(() => client.GetStringAsync("http://127.0.0.1:9/x"));
        ex.ToString().ShouldContain("not allowed");
    }
}
