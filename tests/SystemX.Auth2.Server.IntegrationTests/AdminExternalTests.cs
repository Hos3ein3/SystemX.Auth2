using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.External;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>Sends each request to the fake OIDC provider or the fake SAML identity provider.</summary>
public sealed class CompositeHandler(FakeOidcProvider oidc, FakeSamlIdp saml) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        request.RequestUri!.Host == "idp.test" ? new HttpMessageInvoker(saml).SendAsync(request, ct) : new HttpMessageInvoker(oidc).SendAsync(request, ct);
}

public abstract class AdminExternalBase : AdminBase
{
    protected readonly FakeOidcProvider Oidc = new();
    protected readonly FakeSamlIdp SamlIdp = new();

    protected override void Customize(IServiceCollection services)
    {
        services.AddHttpClient(ExternalHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => new CompositeHandler(Oidc, SamlIdp));
        services.Configure<Auth2ServerOptions>(o => { o.Issuer = "https://auth.test"; o.Passkeys.ServerDomain = "auth.test"; });
    }

    protected static object GoogleBody(string name = "google", string secret = "super-secret-value", bool enabled = true, object? extra = null) => new Dictionary<string, object?>
    {
        ["kind"] = "oidc", ["name"] = name, ["type"] = "Google", ["enabled"] = enabled, ["clientId"] = "id-1", ["clientSecret"] = secret, ["displayName"] = "Google",
    };

    protected string P(Guid id, string tail = "") => $"/applications/{id}/external-providers{tail}";

    protected async Task<(HttpStatusCode Status, JsonElement Body, string Text)> CallAsync(HttpMethod m, string path, string? token, object? body = null, params (string, string)[] headers)
    {
        var r = await new Browser(Factory).SendJsonAsync(m, Admin + path, body, token, headers);
        var text = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    protected string Version(int v) => $"\"v{v}\"";
}

public class AdminExternalProviderTests : AdminExternalBase
{
    [Fact]
    public async Task Types_ListEveryProviderTypeWithItsFields()
    {
        await StartAsync();
        var r = await GetA("/external-provider-types", ProjectToken);
        r.Status.ShouldBe(HttpStatusCode.OK);
        r.Body.GetArrayLength().ShouldBe(8);
        r.Body.EnumerateArray().Select(t => t.Str("type")).ShouldBe(["Google", "Microsoft", "GitHub", "Apple", "Facebook", "GenericOidc", "Saml", "Ldap"], ignoreOrder: true);
        var google = r.Body.EnumerateArray().First(t => t.Str("type") == "Google");
        google.GetProperty("fields").EnumerateArray().Select(f => f.Str("name")).ShouldContain("HostedDomain");
        google.GetProperty("emailVerifiedByDefault").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Create_ShowsUrls_NeverShowsASecret_AndTheProviderIsLiveAtOnce()
    {
        await StartAsync();
        var r = await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody());
        r.Status.ShouldBe(HttpStatusCode.Created, r.Text);
        r.Body.GetProperty("secretIsSet").GetProperty("clientSecret").GetBoolean().ShouldBeTrue();
        r.Body.GetProperty("urls").Str("callback").ShouldBe("https://auth.test/callback/login/appa/google");
        r.Body.Str("registrationName").ShouldBe($"ext-{A.Id:N}-google");
        r.Text.ShouldNotContain("super-secret-value");
        r.Text.ToLowerInvariant().ShouldNotContain("protected");

        var list = await CallAsync(HttpMethod.Get, P(A.Id), ProjectToken);
        list.Text.ShouldNotContain("super-secret-value");
        list.Text.ToLowerInvariant().ShouldNotContain("protected");
        list.Body.GetArrayLength().ShouldBe(1);

        // the database holds an encrypted value, and the login page list shows the provider without any change of the Server
        await using (var scope = NewScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var raw = await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Database.SqlQuery<string>($"select settings_json::text as \"Value\" from applications where id = {A.Id}").SingleAsync();
            raw.ShouldNotContain("super-secret-value");
        }
        var providers = await (await new Browser(Factory).GetAsync("/connect/external/providers?client_id=appa")).JsonAsync();
        providers.GetProperty("providers").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task EverySettingsRoute_HidesTheSecrets_AndTheGenericPutKeepsTheProviders()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody());
        var got = await GetA($"/applications/{A.Id}/settings", GlobalToken);
        var get = (got.Status, got.Body, Text: await got.Response.Content.ReadAsStringAsync());
        var etag = got.Response.Headers.ETag!.Tag;
        get.Text.ShouldNotContain("super-secret-value");
        get.Text.ToLowerInvariant().ShouldNotContain("secretprotected");
        get.Body.GetProperty("externalProviders").GetArrayLength().ShouldBe(1);

        // a PUT whose body has no providers (or others) does not touch them
        var edited = JsonDocument.Parse(get.Text).RootElement.Clone();
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(edited.GetRawText())!;
        body["externalProviders"] = JsonDocument.Parse("[]").RootElement.Clone();
        var put = await CallAsync(HttpMethod.Put, $"/applications/{A.Id}/settings", GlobalToken, body, ("If-Match", etag));
        put.Status.ShouldBe(HttpStatusCode.NoContent, put.Text);
        (await CallAsync(HttpMethod.Get, P(A.Id), GlobalToken)).Body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ProjectAdmin_MayManageTheOwnProviders_ButNotTheGlobalOnlyTrustFlags_AndNotOtherApplications()
    {
        await StartAsync();
        (await CallAsync(HttpMethod.Post, P(A.Id), ProjectToken, GoogleBody("mine"))).Status.ShouldBe(HttpStatusCode.Created);
        var trusting = new Dictionary<string, object?> { ["kind"] = "oidc", ["name"] = "g2", ["type"] = "GenericOidc", ["clientId"] = "c", ["trustProviderEmailVerification"] = true };
        var denied = await CallAsync(HttpMethod.Post, P(A.Id), ProjectToken, trusting);
        denied.Status.ShouldBe(HttpStatusCode.Forbidden);
        denied.Body.Str("code").ShouldBe(Auth2ErrorCodes.Forbidden);
        (await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, trusting)).Status.ShouldBe(HttpStatusCode.Created);

        (await CallAsync(HttpMethod.Get, P(B.Id), ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await CallAsync(HttpMethod.Post, P(B.Id), ProjectToken, GoogleBody("sneaky"))).Status.ShouldBe(HttpStatusCode.NotFound);
        (await CallAsync(HttpMethod.Get, P(A.Id), PlainToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(HttpMethod.Get, P(A.Id), null)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_RejectsInvalidSettings_AndDuplicates()
    {
        await StartAsync();
        var bad = await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, new { kind = "oidc", name = "Bad Name", type = "GenericOidc", clientId = "c", authority = "http://insecure.test", enabled = true, clientSecret = "s" });
        bad.Status.ShouldBe(HttpStatusCode.BadRequest);
        bad.Body.Str("code").ShouldBe(Auth2ErrorCodes.ValidationFailed);
        (await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody())).Status.ShouldBe(HttpStatusCode.Created);
        (await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody())).Status.ShouldBe(HttpStatusCode.Conflict);
        (await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, "not json")).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody("enabled-without-secret", secret: "", enabled: true))).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_NeedsTheRowVersion_KeepsTheOldSecret_AndCannotRenameOrChangeTheType()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody());   // settings version is 3 now (two applications were set up first)
        var current = (await CallAsync(HttpMethod.Get, $"/applications/{A.Id}", GlobalToken)).Body.GetProperty("settingsVersion").GetInt32();

        var noVersion = await CallAsync(HttpMethod.Put, P(A.Id, "/google"), GlobalToken, GoogleBody(secret: ""));
        noVersion.Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Put, P(A.Id, "/google"), GlobalToken, GoogleBody(secret: ""), ("If-Match", Version(current - 1)))).Status.ShouldBe(HttpStatusCode.Conflict);

        var ok = await CallAsync(HttpMethod.Put, P(A.Id, "/google"), GlobalToken, new Dictionary<string, object?> { ["kind"] = "oidc", ["name"] = "google", ["type"] = "Google", ["enabled"] = true, ["clientId"] = "id-2", ["displayName"] = "New text" }, ("If-Match", Version(current)));
        ok.Status.ShouldBe(HttpStatusCode.OK, ok.Text);
        ok.Body.Str("displayName").ShouldBe("New text");
        ok.Body.GetProperty("secretIsSet").GetProperty("clientSecret").GetBoolean().ShouldBeTrue();   // an empty secret keeps the old one

        var next = current + 1;
        (await CallAsync(HttpMethod.Put, P(A.Id, "/google"), GlobalToken, GoogleBody("renamed"), ("If-Match", Version(next)))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Put, P(A.Id, "/google"), GlobalToken, new { kind = "oidc", name = "google", type = "Facebook", clientId = "x" }, ("If-Match", Version(next)))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Put, P(A.Id, "/nope"), GlobalToken, GoogleBody("nope"), ("If-Match", Version(next)))).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RotateSecret_ReplacesIt_AndTheNewSecretIsUsedAtTheProvider()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody(secret: "old-secret"));
        var r = await CallAsync(HttpMethod.Post, P(A.Id, "/google/rotate-secret"), GlobalToken, new { clientSecret = "new-secret-value" });
        r.Status.ShouldBe(HttpStatusCode.NoContent);
        (await CallAsync(HttpMethod.Post, P(A.Id, "/google/rotate-secret"), GlobalToken, new { })).Status.ShouldBe(HttpStatusCode.BadRequest);
        var registry = Factory.Services.GetRequiredService<IExternalProviderRegistry>();
        var protectedValue = ((OidcProviderSettings)registry.Get(A.Id, "google")!.Settings).ClientSecretProtected;
        Factory.Services.GetRequiredService<IExternalSecretProtector>().Unprotect(protectedValue).ShouldBe("new-secret-value");
        (await EventsAsyncFor("external.provider.secret_rotated")).ShouldContain(e => e.TargetId == "google");
    }

    [Fact]
    public async Task Delete_RemovesTheProvider_ButTheUserLinksStay()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody());
        var user = await CreateUserInAsync("appa", "linked");
        await InAsync("appa", async m => await m.AddLoginAsync((await m.FindByNameAsync("linked"))!, new UserLoginInfo("google", "g-1", "Google")));
        (await CallAsync(HttpMethod.Delete, P(A.Id, "/google"), GlobalToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await CallAsync(HttpMethod.Delete, P(A.Id, "/google"), GlobalToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        Factory.Services.GetRequiredService<IExternalProviderRegistry>().Get(A.Id, "google").ShouldBeNull();
        (await CallAsync(HttpMethod.Get, $"/users/{user.Id}/external-logins", GlobalToken, null, ("X-Auth2-Application", "appa"))).Body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task EveryWrite_IsAudited_WithoutSecrets()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody(secret: "audit-me-not"));
        await CallAsync(HttpMethod.Post, P(A.Id, "/google/rotate-secret"), GlobalToken, new { clientSecret = "audit-me-not-2" });
        await CallAsync(HttpMethod.Delete, P(A.Id, "/google"), GlobalToken);
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var events = await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().AuditEvents.Where(e => e.Action.StartsWith("external.provider")).ToListAsync();
        events.Select(e => e.Action).ShouldBe(["external.provider.created", "external.provider.secret_rotated", "external.provider.deleted"], ignoreOrder: true);
        events.Any(e => (e.DataJson ?? "").Contains("audit-me-not")).ShouldBeFalse();
    }

    [Fact]
    public async Task Test_OidcShowsTheEndpoints_AndAFailureShowsTheReasonOnly()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, new { kind = "oidc", name = "corp", type = "GenericOidc", clientId = "c", clientSecret = "s", authority = "https://idp.corp.test", enabled = true });
        var ok = await CallAsync(HttpMethod.Post, P(A.Id, "/corp/test"), ProjectToken);
        ok.Status.ShouldBe(HttpStatusCode.OK);
        ok.Body.GetProperty("ok").GetBoolean().ShouldBeTrue();
        ok.Body.GetProperty("details").Str("issuer").ShouldBe("https://idp.corp.test");
        ok.Body.GetProperty("details").Str("flow").ShouldContain("PKCE");

        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, new { kind = "oidc", name = "broken", type = "GenericOidc", clientId = "c", clientSecret = "s", authority = "https://idp.corp.test/x", enabled = false });
        Oidc.IssuerOverride = "https://wrong.test";
        var bad = await CallAsync(HttpMethod.Post, P(A.Id, "/broken/test"), ProjectToken);
        bad.Body.GetProperty("ok").GetBoolean().ShouldBeFalse();
        bad.Body.Str("reason").ShouldBe("discovery_failed");
        (await CallAsync(HttpMethod.Post, P(A.Id, "/missing/test"), ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<List<AuditEvent>> EventsAsyncFor(string action)
    {
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().AuditEvents.Where(e => e.Action == action).ToListAsync();
    }
}

public class AdminSamlTests : AdminExternalBase
{
    private object SamlBody(string name = "corp-saml") => new Dictionary<string, object?> { ["kind"] = "saml", ["name"] = name, ["type"] = "Saml", ["enabled"] = true, ["idpMetadataUrl"] = FakeSamlIdp.MetadataUrl };

    [Fact]
    public async Task Create_ShowsAcsSloAndMetadataUrls_AndTestShowsTheCertificates()
    {
        await StartAsync();
        var r = await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, SamlBody());
        r.Status.ShouldBe(HttpStatusCode.Created, r.Text);
        r.Body.GetProperty("urls").Str("acs").ShouldBe("https://auth.test/saml/appa/corp-saml/acs");
        r.Body.GetProperty("urls").Str("metadata").ShouldBe("https://auth.test/saml/appa/corp-saml/metadata");
        r.Body.GetProperty("urls").Str("slo").ShouldBe("https://auth.test/saml/appa/corp-saml/slo");

        var test = await CallAsync(HttpMethod.Post, P(A.Id, "/corp-saml/test"), ProjectToken);
        test.Body.GetProperty("ok").GetBoolean().ShouldBeTrue();
        test.Body.GetProperty("details").Str("entityId").ShouldBe(FakeSamlIdp.EntityId);
        test.Body.GetProperty("details").Str("certificate1").ShouldContain("expires");
    }

    [Fact]
    public async Task RefreshMetadata_ReadsItAgain_AndGenerateCertificate_ReturnsOnlyThePublicPart()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, SamlBody());
        await CallAsync(HttpMethod.Post, P(A.Id, "/corp-saml/test"), GlobalToken);
        var before = SamlIdp.MetadataRequests;
        (await CallAsync(HttpMethod.Post, P(A.Id, "/corp-saml/saml/refresh-metadata"), ProjectToken)).Status.ShouldBe(HttpStatusCode.OK);
        SamlIdp.MetadataRequests.ShouldBeGreaterThan(before);

        var cert = await CallAsync(HttpMethod.Post, P(A.Id, "/corp-saml/saml/generate-sp-certificate"), ProjectToken);
        cert.Status.ShouldBe(HttpStatusCode.OK, cert.Text);
        cert.Body.Str("certificatePem").ShouldContain("BEGIN CERTIFICATE");
        cert.Text.ShouldNotContain("PRIVATE KEY");
        cert.Body.GetProperty("notAfter").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddYears(2));
        var list = await CallAsync(HttpMethod.Get, P(A.Id), ProjectToken);
        list.Body[0].GetProperty("secretIsSet").GetProperty("spCertificate").GetBoolean().ShouldBeTrue();
        list.Text.ShouldNotContain("Protected");
        // the metadata now has the public certificate
        (await (await new Browser(Factory).GetAsync("/saml/appa/corp-saml/metadata")).Content.ReadAsStringAsync()).ShouldContain("X509Certificate");
    }

    [Fact]
    public async Task SamlOnlyRoutes_AreNotFoundForOtherTypes()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody());
        (await CallAsync(HttpMethod.Post, P(A.Id, "/google/saml/generate-sp-certificate"), GlobalToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await CallAsync(HttpMethod.Post, P(A.Id, "/google/ldap/test-user"), GlobalToken, new { userName = "x" })).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DisabledSamlWithAllowIdpInitiated_NeedsAGlobalAdmin()
    {
        await StartAsync();
        var body = new Dictionary<string, object?> { ["kind"] = "saml", ["name"] = "idpi", ["type"] = "Saml", ["enabled"] = true, ["idpMetadataUrl"] = FakeSamlIdp.MetadataUrl, ["allowIdpInitiated"] = true };
        (await CallAsync(HttpMethod.Post, P(A.Id), ProjectToken, body)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, body)).Status.ShouldBe(HttpStatusCode.Created);
    }
}

public class AdminMappingAndDomainTests : AdminExternalBase
{
    private async Task<(Guid Role, Guid Group, Guid Perm, Guid OtherRole, Guid GlobalAdmin)> SeedAsync()
    {
        await using var scope = ScopeFor("appa");
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        await access.CreateRoleAsync("editor");
        await scope.ServiceProvider.GetRequiredService<IGroupService>().CreateAsync("finance");
        await access.CreatePermissionAsync(new SystemX.Auth2.Contracts.Access.PermissionDefinition("reports.read"));
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        Guid globalAdmin, other;
        await using (var s2 = NewScope())
        {
            using var _ = s2.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var d2 = s2.ServiceProvider.GetRequiredService<Auth2DbContext>();
            globalAdmin = await d2.Roles.Where(r => r.Name == Auth2Roles.GlobalAdmin).Select(r => r.Id).SingleAsync();
            var r = new AppRole("b-role") { ProjectId = B.Id };
            d2.Roles.Add(r);
            await d2.SaveChangesAsync();
            other = r.Id;
        }
        return (await db.Roles.Where(r => r.Name == "editor").Select(r => r.Id).SingleAsync(), await db.Groups.Select(g => g.Id).SingleAsync(), await db.Permissions.Select(p => p.Id).SingleAsync(), other, globalAdmin);
    }

    [Fact]
    public async Task Mappings_CanBeCreatedChangedListedAndDeleted()
    {
        await StartAsync();
        var (role, group, perm, _, _) = await SeedAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody("corp"));
        var path = $"/applications/{A.Id}/external-group-mappings";
        var created = await CallAsync(HttpMethod.Post, path, ProjectToken, new { providerName = "corp", sourceType = "Group", sourceName = "", matchType = "Prefix", sourceValue = "Fin-", targetType = "Role", targetId = role });
        created.Status.ShouldBe(HttpStatusCode.Created, created.Text);
        var id = created.Body.Str("id");
        (await CallAsync(HttpMethod.Get, path, ProjectToken)).Body.GetArrayLength().ShouldBe(1);
        var updated = await CallAsync(HttpMethod.Put, $"{path}/{id}", ProjectToken, new { providerName = (string?)null, sourceType = "Claim", sourceName = "dept", matchType = "Exact", sourceValue = "audit", targetType = "Permission", targetId = perm });
        updated.Status.ShouldBe(HttpStatusCode.OK, updated.Text);
        updated.Body.Str("targetType").ShouldBe("Permission");
        (await CallAsync(HttpMethod.Post, path, ProjectToken, new { sourceType = "Group", sourceName = "", matchType = "Exact", sourceValue = "x", targetType = "Group", targetId = group })).Status.ShouldBe(HttpStatusCode.Created);
        (await CallAsync(HttpMethod.Delete, $"{path}/{id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await CallAsync(HttpMethod.Delete, $"{path}/{id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await CallAsync(HttpMethod.Get, path, ProjectToken)).Body.GetArrayLength().ShouldBe(1);
        (await CallAsync(HttpMethod.Get, $"/applications/{B.Id}/external-group-mappings", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Mappings_RefuseGlobalAdmin_SystemRoles_OtherApplications_RegexAndUnknownProviders()
    {
        await StartAsync();
        var (role, _, _, otherRole, globalAdmin) = await SeedAsync();
        var path = $"/applications/{A.Id}/external-group-mappings";
        object Map(Guid target, string match = "Exact", string? provider = null, string type = "Role") =>
            new { providerName = provider, sourceType = "Group", sourceName = "", matchType = match, sourceValue = "x", targetType = type, targetId = target };
        (await CallAsync(HttpMethod.Post, path, GlobalToken, Map(globalAdmin))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Post, path, GlobalToken, Map(otherRole))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Post, path, GlobalToken, Map(role, match: "Regex"))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Post, path, GlobalToken, Map(role, provider: "ghost"))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Post, path, GlobalToken, Map(role, type: "Group"))).Status.ShouldBe(HttpStatusCode.BadRequest);   // a role id is not a group id
        (await CallAsync(HttpMethod.Post, path, GlobalToken, Map(role))).Status.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task EmailDomainRoutes_AreReplacedAsAWhole_AndADomainHasOneProvider()
    {
        await StartAsync();
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody("one"));
        await CallAsync(HttpMethod.Post, P(A.Id), GlobalToken, GoogleBody("two"));
        var path = $"/applications/{A.Id}/email-domain-routes";
        (await CallAsync(HttpMethod.Put, path, ProjectToken, new[] { new { providerName = "one", domains = new[] { "Contoso.com" } }, new { providerName = "two", domains = new[] { "fabrikam.com" } } })).Status.ShouldBe(HttpStatusCode.NoContent);
        var list = await CallAsync(HttpMethod.Get, path, ProjectToken);
        list.Body.EnumerateArray().First(r => r.Str("providerName") == "one").GetProperty("domains")[0].GetString().ShouldBe("contoso.com");
        var hint = await (await new Browser(Factory).GetAsync("/connect/external/providers?client_id=appa&hint=a@fabrikam.com")).JsonAsync();
        hint.Str("suggestedProvider").ShouldBe("two");

        var dup = await CallAsync(HttpMethod.Put, path, ProjectToken, new[] { new { providerName = "one", domains = new[] { "x.com" } }, new { providerName = "two", domains = new[] { "x.com" } } });
        dup.Status.ShouldBe(HttpStatusCode.BadRequest);
        (await CallAsync(HttpMethod.Put, path, ProjectToken, new[] { new { providerName = "ghost", domains = new[] { "x.com" } } })).Status.ShouldBe(HttpStatusCode.BadRequest);
        // the failed calls changed nothing
        (await CallAsync(HttpMethod.Get, path, ProjectToken)).Body.EnumerateArray().First(r => r.Str("providerName") == "two").GetProperty("domains")[0].GetString().ShouldBe("fabrikam.com");
    }
}

public class AdminUserExternalTests : AdminExternalBase
{
    [Fact]
    public async Task ExternalLogins_CanBeListedAndRemoved_AndTheLastOneNeedsAGlobalAdminToForce()
    {
        await StartAsync();
        var user = await CreateUserInAsync("appa", "linked");
        await InAsync("appa", async m => await m.RemovePasswordAsync((await m.FindByNameAsync("linked"))!));
        await InAsync("appa", async m => await m.AddLoginAsync((await m.FindByNameAsync("linked"))!, new UserLoginInfo("corp", "key-1", "Corp")));
        var path = $"/users/{user.Id}/external-logins";
        var list = await CallAsync(HttpMethod.Get, path, ProjectToken);
        list.Body[0].Str("provider").ShouldBe("corp");
        list.Body[0].Str("providerKey").ShouldBe("key-1");

        var refused = await CallAsync(HttpMethod.Delete, path + "/corp", ProjectToken);
        refused.Status.ShouldBe(HttpStatusCode.BadRequest);
        refused.Body.Str("code").ShouldBe(Auth2ErrorCodes.CannotRemoveLastLoginMethod);
        (await CallAsync(HttpMethod.Delete, path + "/corp?force=true", ProjectToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await CallAsync(HttpMethod.Delete, path + "/corp?force=true", GlobalToken, null, ("X-Auth2-Application", "appa"))).Status.ShouldBe(HttpStatusCode.NoContent);
        (await CallAsync(HttpMethod.Delete, path + "/corp", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UsersOfOtherApplications_DoNotExistForAProjectAdmin()
    {
        await StartAsync();
        var other = await CreateUserInAsync("appb", "bob");
        (await CallAsync(HttpMethod.Get, $"/users/{other.Id}/external-logins", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await CallAsync(HttpMethod.Get, $"/users/{other.Id}/passkeys", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Passkeys_CanBeListedAndRemoved_AndTheBlockedListIsForGlobalAdminsOnly()
    {
        await StartAsync();
        await UpdateSettingsAsync(A, s => { s.Methods.AllowedLoginMethods.Add(LoginMethod.Passkey); s.Passkeys.AllowPasskeys = true; s.RedirectUris.Add("https://client.test/cb"); });
        var session = new Browser(Factory);
        (await session.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "plain", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var key = new SoftwareAuthenticator("https://auth.test");
        var options = await session.SendJsonWithTokenAsync(HttpMethod.Post, "/account/passkey/creation-options?client_id=appa", new { });
        var credential = JsonDocument.Parse(key.Create(await options.Content.ReadAsStringAsync())).RootElement;
        var registered = await session.SendJsonWithTokenAsync(HttpMethod.Post, "/account/passkey/register?client_id=appa", new { credential, name = "Key" });
        registered.StatusCode.ShouldBe(HttpStatusCode.OK, await registered.Content.ReadAsStringAsync() + await options.Content.ReadAsStringAsync());
        var id = (await registered.JsonAsync()).Str("id");
        var plain = (await FindUserAsync2("plain"))!;

        var list = await CallAsync(HttpMethod.Get, $"/users/{plain.Id}/passkeys", ProjectToken);
        list.Body.GetArrayLength().ShouldBe(1);
        (await CallAsync(HttpMethod.Delete, $"/users/{plain.Id}/passkeys/{id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await CallAsync(HttpMethod.Delete, $"/users/{plain.Id}/passkeys/{id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);

        (await CallAsync(HttpMethod.Get, "/passkeys/blocked-aaguids", ProjectToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
        var blocked = Guid.Parse("11111111-2222-3333-4444-555555555555");
        (await CallAsync(HttpMethod.Put, "/passkeys/blocked-aaguids", GlobalToken, new { aaguids = new[] { blocked } })).Status.ShouldBe(HttpStatusCode.NoContent);
        (await CallAsync(HttpMethod.Get, "/passkeys/blocked-aaguids", GlobalToken)).Body.GetProperty("aaguids")[0].GetGuid().ShouldBe(blocked);
        var again = await session.SendJsonWithTokenAsync(HttpMethod.Post, "/account/passkey/creation-options?client_id=appa", new { });
        var credential2 = JsonDocument.Parse(new SoftwareAuthenticator("https://auth.test").Create(await again.Content.ReadAsStringAsync())).RootElement;
        var denied = await session.SendJsonWithTokenAsync(HttpMethod.Post, "/account/passkey/register?client_id=appa", new { credential = credential2, name = "Blocked" });
        (await denied.JsonAsync()).Str("code").ShouldBe(Auth2ErrorCodes.PasskeyBlockedAuthenticator);
    }

    private async Task<AppUser?> FindUserAsync2(string name)
    {
        await using var scope = NewScope();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Users.FirstOrDefaultAsync(u => u.UserName == name);
    }
}
