using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Sync;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SystemX.Auth2.Client.Tests;

public class AnyOfRuleTests
{
    // ---- the attributes

    [Fact]
    public void TheAttributes_MakeTheirPolicyFromTheNamedValues_AndKeepThemForTheScanners()
    {
        var a = new Auth2AnyOfAttribute { Roles = ["Admin", "Editor"], Permissions = ["orders:write"], Groups = ["finance"], Scopes = ["reports.read"] };
        ((IAuthorizeData)a).Policy.ShouldBe("auth2:any:any:r=Admin|Editor;p=orders:write;g=finance;s=reports.read");
        var meta = (IAuth2AnyOfMetadata)a;
        meta.Roles.ShouldBe(["Admin", "Editor"]);
        meta.Permissions.ShouldBe(["orders:write"]);
        meta.Groups.ShouldBe(["finance"]);
        meta.Scopes.ShouldBe(["reports.read"]);

        ((IAuthorizeData)new Auth2RoleOrPermissionAttribute { Roles = ["Admin"], Permissions = ["a.b"] }).Policy.ShouldBe("auth2:any:any:r=Admin;p=a.b");
        ((IAuthorizeData)new Auth2RoleOrGroupAttribute { Roles = ["Admin"], Groups = ["g"] }).Policy.ShouldBe("auth2:any:any:r=Admin;g=g");
        ((IAuthorizeData)new Auth2PermissionOrGroupAttribute { Permissions = ["a.b"], Groups = ["g"] }).Policy.ShouldBe("auth2:any:any:p=a.b;g=g");
        ((IAuthorizeData)new Auth2ScopeOrRoleAttribute { Scopes = ["s"], Roles = ["Admin"] }).Policy.ShouldBe("auth2:any:any:r=Admin;s=s");
        ((IAuthorizeData)new Auth2AnyOfAttribute()).Policy.ShouldBeNull();   // nothing named: a signed-in user, like an attribute with no key
    }

    [Fact]
    public async Task ThePolicyProvider_TakesTheNameApart_AlsoWithAColonInAKey()
    {
        var provider = new Auth2PolicyProvider(MsOptions.Create(new AuthorizationOptions()));
        var policy = (await provider.GetPolicyAsync("auth2:any:any:r=Admin|Blog Editor;p=orders:write;g=finance;s=reports.read"))!;
        var r = policy.Requirements.OfType<Auth2AnyOfRequirement>().Single();
        r.Roles.ShouldBe(["Admin", "Blog Editor"]);
        r.Permissions.ShouldBe(["orders:write"]);
        r.Groups.ShouldBe(["finance"]);
        r.Scopes.ShouldBe(["reports.read"]);
        policy.Requirements.ShouldContain(x => x is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    // ---- the handler

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), .. claims.Select(c => new Claim(c.Type, c.Value))], "test", "name", "role"));

    private static Auth2Access Access(string[]? perms = null, string[]? direct = null, string[]? viaRole = null, string[]? groups = null, string[]? roles = null) =>
        new(roles ?? [], perms ?? [], groups ?? [], 1, direct ?? [], viaRole ?? []);

    private static async Task<(bool Passed, IAuth2AccessProvider Provider)> RunAsync(Auth2AnyOfRequirement requirement, ClaimsPrincipal user, Auth2Access? access = null, Exception? fail = null, object? resource = null)
    {
        var provider = Substitute.For<IAuth2AccessProvider>();
        if (fail is not null) provider.GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).ThrowsAsync(fail);
        else provider.GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(access ?? Auth2Access.Empty);
        var context = new AuthorizationHandlerContext([requirement], user, resource);
        await new Auth2AnyOfHandler(provider, NullLogger<Auth2AnyOfHandler>.Instance).HandleAsync(context);
        return (context.HasSucceeded, provider);
    }

    private static Auth2AnyOfRequirement Rule(string[]? roles = null, string[]? permissions = null, string[]? groups = null, string[]? scopes = null) =>
        new(roles ?? [], permissions ?? [], groups ?? [], scopes ?? []);

    [Fact]
    public async Task OneMatchOfAnyKind_IsEnough_AndNoMatchRefuses()
    {
        var rule = Rule(roles: ["Admin"], permissions: ["orders.write"], groups: ["finance"], scopes: ["reports.read"]);
        (await RunAsync(rule, User(("role", "admin")))).Passed.ShouldBeTrue();                                    // the role (case does not matter)
        (await RunAsync(rule, User(), Access(perms: ["orders.write"]))).Passed.ShouldBeTrue();                      // the permission
        (await RunAsync(rule, User(), Access(groups: ["Finance"]))).Passed.ShouldBeTrue();                          // the group
        (await RunAsync(rule, User(("scope", "openid reports.read")))).Passed.ShouldBeTrue();                       // the scope
        (await RunAsync(rule, User(("role", "Editor")), Access(perms: ["orders.read"], groups: ["hr"]))).Passed.ShouldBeFalse();
        (await RunAsync(rule, User(("scope", "Reports.Read")))).Passed.ShouldBeFalse();                            // scopes are exact
    }

    [Fact]
    public async Task APermissionKey_IsTrimmedAndLowerCased_LikeTheOtherPermissionRules()
    {
        (await RunAsync(Rule(permissions: [" Orders.Write "]), User(), Access(perms: ["orders.write"]))).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task TheCheapChecksComeFirst_SoTheServerIsNotAskedWhenAScopeOrRoleMatched()
    {
        var (passed, provider) = await RunAsync(Rule(roles: ["Admin"], permissions: ["orders.write"], scopes: ["s"]), User(("scope", "s")));
        passed.ShouldBeTrue();
        await provider.DidNotReceive().GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());

        (passed, provider) = await RunAsync(Rule(roles: ["Admin"], permissions: ["orders.write"]), User(("role", "Admin")));
        passed.ShouldBeTrue();
        await provider.DidNotReceive().GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>());   // the role is in the token
    }

    [Fact]
    public async Task WhenTheServerCannotBeAsked_TheTokenPartsStillWork_AndThePermissionAndGroupPartsRefuse()
    {
        var down = new Auth2Exception(null, Auth2ClientErrorCodes.ServerUnavailable, "x", true);
        (await RunAsync(Rule(roles: ["Admin"], permissions: ["a.b"]), User(("role", "Admin")), fail: down)).Passed.ShouldBeTrue();
        (await RunAsync(Rule(roles: ["Admin"], permissions: ["a.b"]), User(("role", "Editor")), fail: down)).Passed.ShouldBeFalse();
        (await RunAsync(Rule(groups: ["g"]), User(), fail: down)).Passed.ShouldBeFalse();
        (await RunAsync(Rule(scopes: ["s"], groups: ["g"]), User(("scope", "s")), fail: down)).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task ARoleThatTheTokenDoesNotCarry_IsReadFromTheAccessData()
    {
        // a token without the roles scope has no role claim: the roles come from the access data, as for [Auth2Role]
        (await RunAsync(Rule(roles: ["Admin"]), User(), Access(roles: ["Admin"]))).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task ThePermissionSourceOfTheEndpoint_AppliesToThePermissionPart()
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new Auth2OnlyRolePermissionAttribute()), "e"));
        var rule = Rule(permissions: ["a.b"]);
        (await RunAsync(rule, User(), Access(perms: ["a.b"], viaRole: ["a.b"]), resource: http)).Passed.ShouldBeTrue();
        (await RunAsync(rule, User(), Access(perms: ["a.b"], direct: ["a.b"]), resource: http)).Passed.ShouldBeFalse();
    }

    // ---- the Minimal API methods

    private static RouteEndpoint Endpoint(Action<RouteHandlerBuilder> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        var app = builder.Build();
        configure(app.MapGet("/x", () => "ok"));
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().Single();
    }

    [Fact]
    public void TheMinimalApiMethods_AddOneAnyOfRuleWithTheirLists()
    {
        void Check(Action<RouteHandlerBuilder> configure, string policy)
        {
            var e = Endpoint(configure);
            e.Metadata.OfType<IAuth2AnyOfMetadata>().Count().ShouldBe(1);
            e.Metadata.OfType<IAuthorizeData>().Single().Policy.ShouldBe(policy);
        }
        Check(b => b.WithRoleOrPermission(["Admin"], ["orders.write"]), "auth2:any:any:r=Admin;p=orders.write");
        Check(b => b.WithRoleOrGroup(["Admin"], ["finance"]), "auth2:any:any:r=Admin;g=finance");
        Check(b => b.WithPermissionOrGroup(["orders.write"], ["finance"]), "auth2:any:any:p=orders.write;g=finance");
        Check(b => b.WithScopeOrRole(["reports.read"], ["Admin"]), "auth2:any:any:r=Admin;s=reports.read");
        Check(b => b.WithAnyOf(roles: ["Admin"], scopes: ["s"]), "auth2:any:any:r=Admin;s=s");
    }

    [Fact]
    public void WithAnyOf_WithNoValueAtAll_IsAnError()
    {
        var builder = WebApplication.CreateBuilder().Build();
        Should.Throw<ArgumentException>(() => builder.MapGet("/x", () => "ok").WithAnyOf());
        Should.Throw<ArgumentException>(() => builder.MapGet("/y", () => "ok").WithRoleOrPermission([], []));
    }

    [Fact]
    public void ChainingAnyOfWithAnotherRule_KeepsBothRules()
    {
        var e = Endpoint(b => b.WithRoleOrPermission(["Admin"], ["orders.write"]).WithScope("reports.read"));
        e.Metadata.OfType<IAuthorizeData>().Select(a => a.Policy).ShouldBe(["auth2:any:any:r=Admin;p=orders.write", "auth2:scope:any:reports.read"]);
    }

    // ---- the scanners

    private static EndpointDataSource Endpoints(params object[] metadata) =>
        new DefaultEndpointDataSource(metadata.Select((m, i) =>
            (Endpoint)new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/a" + i), 0, new EndpointMetadataCollection(m), "a" + i)));

    [Fact]
    public void TheScanners_FindTheValuesOfAnyOfRulesOnEndpoints()
    {
        var rule = new Auth2AnyOfAttribute { Roles = ["Auditor"], Permissions = ["Reports.Read"], Groups = ["finance"], Scopes = ["reports.api", "auth2.me"] };
        var source = Endpoints(rule);
        List<System.Reflection.Assembly> none = [];

        new PermissionScanner([source], MsOptions.Create(new Auth2PermissionOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = none }), NullLogger<PermissionScanner>.Instance)
            .Scan().Select(p => p.Key).ShouldBe(["reports.read"]);
        new RoleScanner([source], MsOptions.Create(new Auth2RoleOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = none }), NullLogger<RoleScanner>.Instance)
            .Scan().Select(r => r.Name).ShouldBe(["Auditor"]);
        new GroupScanner([source], MsOptions.Create(new Auth2GroupOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = none }), NullLogger<GroupScanner>.Instance)
            .Scan().Select(g => g.Name).ShouldBe(["finance"]);
        new ScopeScanner([source], MsOptions.Create(new Auth2ScopeOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = none }), NullLogger<ScopeScanner>.Instance)
            .Scan().Select(s => s.Name).ShouldBe(["reports.api"]);   // auth2.* is reserved and skipped silently
    }

    /// <summary>An assembly of its own with two methods that carry any-of attributes (a type in this test assembly would show up in the other scan tests).</summary>
    private static System.Reflection.Assembly AssemblyWithAttributes()
    {
        var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("AnyOfDynamic"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var type = asm.DefineDynamicModule("m").DefineType("Dyn", System.Reflection.TypeAttributes.Public);

        void Method(string name, Type attribute, params (string Property, string[] Values)[] named)
        {
            var method = type.DefineMethod(name, System.Reflection.MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            method.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
            method.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(attribute.GetConstructor(Type.EmptyTypes)!, [],
                [.. named.Select(n => attribute.GetProperty(n.Property)!)], [.. named.Select(n => (object)n.Values)]));
        }
        Method("All", typeof(Auth2AnyOfAttribute), ("Roles", ["dyn-role"]), ("Permissions", ["Dyn.Perm"]), ("Groups", ["dyn-group"]), ("Scopes", ["dyn.scope"]));
        Method("Two", typeof(Auth2RoleOrPermissionAttribute), ("Roles", ["dyn-role-2"]), ("Permissions", ["dyn.perm.2"]));
        type.CreateType();
        return asm;
    }

    [Fact]
    public void TheScanners_FindAnyOfAttributesInTheAssembliesToScan()
    {
        List<System.Reflection.Assembly> here = [AssemblyWithAttributes()];
        var empty = new DefaultEndpointDataSource();
        new PermissionScanner([empty], MsOptions.Create(new Auth2PermissionOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = here }), NullLogger<PermissionScanner>.Instance)
            .Scan().Select(p => p.Key).ShouldBe(["dyn.perm", "dyn.perm.2"]);
        new RoleScanner([empty], MsOptions.Create(new Auth2RoleOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = here }), NullLogger<RoleScanner>.Instance)
            .Scan().Select(r => r.Name).ShouldBe(["dyn-role", "dyn-role-2"]);
        new GroupScanner([empty], MsOptions.Create(new Auth2GroupOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = here }), NullLogger<GroupScanner>.Instance)
            .Scan().Select(g => g.Name).ShouldBe(["dyn-group"]);
        new ScopeScanner([empty], MsOptions.Create(new Auth2ScopeOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = here }), NullLogger<ScopeScanner>.Instance)
            .Scan().Select(s => s.Name).ShouldBe(["dyn.scope"]);
    }

    [Fact]
    public void AnyOfValues_DoNotHideTheTextsOfASingleKindAttribute()
    {
        // the same permission named in an any-of rule (no texts) and in [Auth2Permission] with texts: the texts win
        var source = Endpoints(new Auth2RoleOrPermissionAttribute { Permissions = ["orders.write"] }, new Auth2PermissionAttribute("orders.write") { DisplayName = "Write orders" });
        var found = new PermissionScanner([source], MsOptions.Create(new Auth2PermissionOptions { ScanningPolicy = ScanningPolicy.AssemblyScan, AssembliesToScan = [] }), NullLogger<PermissionScanner>.Instance).Scan();
        found.Single().DisplayName.ShouldBe("Write orders");
    }
}
