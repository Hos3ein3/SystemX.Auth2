using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Contracts.Users;

namespace SystemX.Auth2.Client.Tests;

public class AttributeAndPolicyTests
{
    [Fact]
    public void Attributes_AreAuthorizeData_WithPolicyNames_AndKeepTheirValues()
    {
        var p = new Auth2PermissionAttribute("orders.read", "orders.write");
        p.Policy.ShouldBe("auth2:perm:any:orders.read|orders.write");
        ((IAuth2PermissionMetadata)p).Permissions.ShouldBe(["orders.read", "orders.write"]);
        p.RequireAll.ShouldBeFalse();
        new Auth2PermissionAttribute(true, "a").Policy.ShouldBe("auth2:perm:all:a");
        new Auth2RoleAttribute(true, "Admin").Policy.ShouldBe("auth2:role:all:Admin");
        ((IAuth2RoleMetadata)new Auth2RoleAttribute("Admin")).Roles.ShouldBe(["Admin"]);
        new Auth2ScopeAttribute("a.read").Policy.ShouldBe("auth2:scope:any:a.read");
        new Auth2GroupAttribute("finance").Policy.ShouldBe("auth2:group:any:finance");
        new Auth2MachineOnlyAttribute().Policy.ShouldBe("auth2:kind:machine:");
        new Auth2UserOnlyAttribute().Policy.ShouldBe("auth2:kind:user:");
        new Auth2OnlyRolePermissionAttribute().Source.ShouldBe(Auth2PermissionSource.RoleOnly);
        new Auth2OnlyUserPermissionAttribute().Source.ShouldBe(Auth2PermissionSource.UserOnly);
        new Auth2AuditAttribute("x").Action.ShouldBe("x");
        new Auth2AnonymousAttribute().ShouldBeAssignableTo<Microsoft.AspNetCore.Authorization.IAllowAnonymous>();
    }

    [Fact]
    public void AnAttributeWithNoValues_IsAnError() =>
        // No key is allowed now: on a const field the attribute only carries the data of the permission (the value of the field is the key).
        new Auth2PermissionAttribute().Permissions.ShouldBeEmpty();

    [Fact]
    public async Task PolicyProvider_BuildsAuth2Policies_AndLeavesOtherNamesToTheDefaultProvider()
    {
        var options = MsOptions.Create(new AuthorizationOptions());
        options.Value.AddPolicy("mine", p => p.RequireClaim("x"));
        var provider = new Auth2PolicyProvider(options);

        var policy = (await provider.GetPolicyAsync("auth2:perm:all:a|b"))!;
        var r = policy.Requirements.OfType<Auth2Requirement>().Single();
        (r.Kind, r.RequireAll).ShouldBe((Auth2RequirementKind.Permission, true));
        r.Values.ShouldBe(["a", "b"]);
        policy.Requirements.ShouldContain(x => x is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);   // anonymous gets 401, not 403
        ReferenceEquals(policy, await provider.GetPolicyAsync("auth2:perm:all:a|b")).ShouldBeTrue();   // cached

        (await provider.GetPolicyAsync("auth2:kind:machine:"))!.Requirements.OfType<Auth2Requirement>().Single().Kind.ShouldBe(Auth2RequirementKind.MachineOnly);
        (await provider.GetPolicyAsync("mine"))!.Requirements.ShouldContain(x => x is Microsoft.AspNetCore.Authorization.Infrastructure.ClaimsAuthorizationRequirement);
        (await provider.GetPolicyAsync("unknown")).ShouldBeNull();
    }
}

public class AuthorizationHandlerTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), .. claims.Select(c => new Claim(c.Type, c.Value))], "test", "name", "role"));

    private static async Task<bool> Run(Auth2Requirement requirement, ClaimsPrincipal user, Auth2Access? access = null, Exception? fail = null, object? resource = null)
    {
        var provider = Substitute.For<IAuth2AccessProvider>();
        if (fail is not null) provider.GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).ThrowsAsync(fail);
        else provider.GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(access ?? Auth2Access.Empty);
        var handler = new Auth2AuthorizationHandler(provider, NullLogger<Auth2AuthorizationHandler>.Instance);
        var context = new AuthorizationHandlerContext([requirement], user, resource);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static Auth2Requirement Perm(bool all, params string[] keys) => new(Auth2RequirementKind.Permission, all, keys);
    private static Auth2Access Access(string[]? perms = null, string[]? direct = null, string[]? role = null, string[]? groups = null, string[]? roles = null) =>
        new(roles ?? [], perms ?? [], groups ?? [], 1, direct ?? [], role ?? []);

    [Fact]
    public async Task Permission_AnyAndAll_AreCaseInsensitiveForTheRequirement()
    {
        var access = Access(perms: ["orders.read"]);
        (await Run(Perm(false, "Orders.Read", "x"), User(), access)).ShouldBeTrue();
        (await Run(Perm(true, "orders.read", "x"), User(), access)).ShouldBeFalse();
        (await Run(Perm(false, "x"), User(), access)).ShouldBeFalse();
        (await Run(Perm(true, "orders.read"), User(), access)).ShouldBeTrue();
    }

    private sealed class FakeResource;

    [Fact]
    public async Task Permission_Source_ComesFromTheEndpointMetadata()
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Microsoft.AspNetCore.Http.Endpoint(null, new Microsoft.AspNetCore.Http.EndpointMetadataCollection(new Auth2OnlyRolePermissionAttribute()), "e"));
        var viaRole = Access(perms: ["a.b"], role: ["a.b"]);
        var direct = Access(perms: ["a.b"], direct: ["a.b"]);
        (await Run(Perm(false, "a.b"), User(), viaRole, resource: http)).ShouldBeTrue();
        (await Run(Perm(false, "a.b"), User(), direct, resource: http)).ShouldBeFalse();

        var userOnly = new DefaultHttpContext();
        userOnly.SetEndpoint(new Microsoft.AspNetCore.Http.Endpoint(null, new Microsoft.AspNetCore.Http.EndpointMetadataCollection(new Auth2OnlyUserPermissionAttribute()), "e"));
        (await Run(Perm(false, "a.b"), User(), direct, resource: userOnly)).ShouldBeTrue();
        (await Run(Perm(false, "a.b"), User(), viaRole, resource: userOnly)).ShouldBeFalse();

        (await Run(Perm(false, "a.b"), User(), direct, resource: new FakeResource())).ShouldBeTrue();   // no endpoint: any source
    }

    [Fact]
    public async Task OlderServer_WithoutSourceLists_FailsClosedForSourceChecks()
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Microsoft.AspNetCore.Http.Endpoint(null, new Microsoft.AspNetCore.Http.EndpointMetadataCollection(new Auth2OnlyUserPermissionAttribute()), "e"));
        (await Run(Perm(false, "a.b"), User(), Auth2Access.From(new CurrentUserInfo(Guid.NewGuid(), "s", null, null, null, null, null, [], ["a.b"], [], [], 1)), resource: http)).ShouldBeFalse();
    }

    [Fact]
    public async Task Roles_AreReadFromTheClaims_CaseInsensitive()
    {
        var user = User(("role", "Editor"));
        (await Run(new(Auth2RequirementKind.Role, false, ["editor", "writer"]), user)).ShouldBeTrue();
        (await Run(new(Auth2RequirementKind.Role, true, ["editor", "writer"]), user)).ShouldBeFalse();
        (await Run(new(Auth2RequirementKind.Role, true, ["Editor"]), user)).ShouldBeTrue();
    }

    [Fact]
    public async Task Scopes_AcceptASpaceSeparatedClaim_OrManyClaims()
    {
        (await Run(new(Auth2RequirementKind.Scope, false, ["b"]), User(("scope", "a b c")))).ShouldBeTrue();
        (await Run(new(Auth2RequirementKind.Scope, false, ["b"]), User(("scope", "a"), ("scope", "b")))).ShouldBeTrue();
        (await Run(new(Auth2RequirementKind.Scope, false, ["b"]), User(("scp", "b")))).ShouldBeTrue();
        (await Run(new(Auth2RequirementKind.Scope, false, ["B"]), User(("scope", "b")))).ShouldBeFalse();   // scopes are case-sensitive
    }

    [Fact]
    public async Task TokenKind_AndGroups()
    {
        (await Run(new(Auth2RequirementKind.MachineOnly, false, []), User((Auth2Claims.Machine, "true")))).ShouldBeTrue();
        (await Run(new(Auth2RequirementKind.MachineOnly, false, []), User())).ShouldBeFalse();
        (await Run(new(Auth2RequirementKind.UserOnly, false, []), User((Auth2Claims.Machine, "true")))).ShouldBeFalse();
        (await Run(new(Auth2RequirementKind.UserOnly, false, []), User())).ShouldBeTrue();
        (await Run(new(Auth2RequirementKind.Group, false, ["Finance"]), User(), Access(groups: ["finance"]))).ShouldBeTrue();
        (await Run(new(Auth2RequirementKind.Group, false, ["hr"]), User(), Access(groups: ["finance"]))).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheServerCannotBeAsked_PermissionsAndGroupsAreRefused()
    {
        var down = new Auth2Exception(null, Auth2ClientErrorCodes.ServerUnavailable, "x", true);
        (await Run(Perm(false, "a"), User(), fail: down)).ShouldBeFalse();
        (await Run(new(Auth2RequirementKind.Group, false, ["g"]), User(), fail: down)).ShouldBeFalse();
        (await Run(new(Auth2RequirementKind.Role, false, ["r"]), User(("role", "r")), fail: down)).ShouldBeTrue();   // claims need no call
    }
}

public class AccessProviderTests
{
    private static ClaimsPrincipal Principal(int pv = 5, bool machine = false) =>
        new(new ClaimsIdentity([new Claim("sub", "11111111-1111-1111-1111-111111111111"), new Claim(Auth2Claims.AccessVersion, pv.ToString()),
            .. machine ? [new Claim(Auth2Claims.Machine, "true")] : Array.Empty<Claim>(), new Claim("role", "Editor")], "test"));

    private static CurrentUserInfo Me(int version, params string[] perms) =>
        new(Guid.NewGuid(), "sara", null, null, null, null, null, ["Editor"], perms, ["g"], [], version, perms, []);

    private static (IAuth2AccessProvider Provider, IAuth2CurrentUserService Users, ICurrentUserStore Store) Build(CurrentUserSource source = CurrentUserSource.TokenAndServer)
    {
        var users = Substitute.For<IAuth2CurrentUserService>();
        var store = new MemoryCurrentUserStore(new MemoryCache(new MemoryCacheOptions()));
        var provider = new AccessProvider(users, store, MsOptions.Create(new Auth2CurrentUserOptions { Source = source }),
            MsOptions.Create(new Auth2ClientOptions { ClientId = "blog" }), NullLogger<AccessProvider>.Instance, TimeProvider.System);
        return (provider, users, store);
    }

    [Fact]
    public async Task OneServerCall_PerRequest_AndThenFromTheStore()
    {
        var (provider, users, store) = Build();
        users.FetchAsync(Arg.Any<ClaimsPrincipal?>(), Arg.Any<CancellationToken>()).Returns(Result<CurrentUserInfo>.Success(Me(5, "a.b")));
        var principal = Principal();
        await provider.GetAccessAsync(principal); await provider.GetAccessAsync(principal);
        await users.Received(1).FetchAsync(Arg.Any<ClaimsPrincipal?>(), Arg.Any<CancellationToken>());

        // a second request (a new provider) finds it in the store
        var second = new AccessProvider(users, store, MsOptions.Create(new Auth2CurrentUserOptions()), MsOptions.Create(new Auth2ClientOptions { ClientId = "blog" }), NullLogger<AccessProvider>.Instance, TimeProvider.System);
        (await second.GetAccessAsync(principal)).Permissions.ShouldBe(["a.b"]);
        await users.Received(1).FetchAsync(Arg.Any<ClaimsPrincipal?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATokenWithANewerVersion_ThanTheCache_ReadsAgain()
    {
        var (provider, users, store) = Build();
        users.FetchAsync(Arg.Any<ClaimsPrincipal?>(), Arg.Any<CancellationToken>()).Returns(Result<CurrentUserInfo>.Success(Me(5, "a.b")), Result<CurrentUserInfo>.Success(Me(9, "a.b", "c.d")));
        await provider.GetAccessAsync(Principal(pv: 5));
        var fresh = new AccessProvider(users, store, MsOptions.Create(new Auth2CurrentUserOptions()), MsOptions.Create(new Auth2ClientOptions { ClientId = "blog" }), NullLogger<AccessProvider>.Instance, TimeProvider.System);
        (await fresh.GetAccessAsync(Principal(pv: 9))).Permissions.ShouldBe(["a.b", "c.d"]);
    }

    [Fact]
    public async Task MachineTokens_AndAnonymous_GetNothing_WithoutACall()
    {
        var (provider, users, _) = Build();
        (await provider.GetAccessAsync(Principal(machine: true))).ShouldBe(Auth2Access.Empty);
        (await provider.GetAccessAsync(new ClaimsPrincipal(new ClaimsIdentity()))).ShouldBe(Auth2Access.Empty);
        await users.DidNotReceive().FetchAsync(Arg.Any<ClaimsPrincipal?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TokenOnly_ReadsRolesFromClaims_AndNeverCalls()
    {
        var (provider, users, _) = Build(CurrentUserSource.TokenOnly);
        var access = await provider.GetAccessAsync(Principal());
        access.Roles.ShouldBe(["Editor"]);
        access.Permissions.ShouldBeEmpty();
        await users.DidNotReceive().FetchAsync(Arg.Any<ClaimsPrincipal?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ServerDown_WithNothingCached_Throws_ButAnOldAnswerIsUsedWhenThereIsOne()
    {
        var (provider, users, store) = Build();
        users.FetchAsync(Arg.Any<ClaimsPrincipal?>(), Arg.Any<CancellationToken>()).Returns(Result<CurrentUserInfo>.Failure(Auth2ClientErrorCodes.ServerUnavailable));
        var ex = await Should.ThrowAsync<Auth2Exception>(() => provider.GetAccessAsync(Principal()));
        ex.IsTransient.ShouldBeTrue();

        await store.SetAsync("auth2:access:blog:11111111-1111-1111-1111-111111111111", new Auth2Access(["R"], ["old.perm"], [], 1, [], []), TimeSpan.FromMinutes(1));
        var another = new AccessProvider(users, store, MsOptions.Create(new Auth2CurrentUserOptions()), MsOptions.Create(new Auth2ClientOptions { ClientId = "blog" }), NullLogger<AccessProvider>.Instance, TimeProvider.System);
        (await another.GetAccessAsync(Principal(pv: 5))).Permissions.ShouldBe(["old.perm"]);   // stale but better than nothing
    }
}

public class StoreTests
{
    [Fact]
    public async Task Memory_And_Distributed_Stores_RoundTrip_AndExpire()
    {
        var access = new Auth2Access(["R"], ["a.b"], ["g"], 3, ["a.b"], []);
        ICurrentUserStore memory = new MemoryCurrentUserStore(new MemoryCache(new MemoryCacheOptions()));
        ICurrentUserStore distributed = new DistributedCurrentUserStore(new MemoryDistributedCache(MsOptions.Create(new MemoryDistributedCacheOptions())));
        foreach (var store in new[] { memory, distributed })
        {
            (await store.GetAsync("k")).ShouldBeNull();
            await store.SetAsync("k", access, TimeSpan.FromMinutes(1));
            (await store.GetAsync("k"))!.Permissions.ShouldBe(["a.b"]);
            (await store.GetAsync("k"))!.AccessVersion.ShouldBe(3);
            await store.RemoveAsync("k");
            (await store.GetAsync("k")).ShouldBeNull();
            await store.SetAsync("short", access, TimeSpan.FromMilliseconds(50));
            await Task.Delay(150);
            (await store.GetAsync("short")).ShouldBeNull();
        }
    }

    [Fact]
    public async Task ABrokenDistributedEntry_IsTreatedAsMissing()
    {
        var cache = new MemoryDistributedCache(MsOptions.Create(new MemoryDistributedCacheOptions()));
        await cache.SetAsync("k", [1, 2, 3]);
        (await new DistributedCurrentUserStore(cache).GetAsync("k")).ShouldBeNull();
    }
}
