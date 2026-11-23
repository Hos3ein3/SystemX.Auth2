using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Client.Sync;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SystemX.Auth2.Client.Tests;

/// <summary>Types with attributes, for the assembly scan.</summary>
[Auth2Permission("blazor.page")]
public sealed class ScanFixturePage
{
    [Auth2Permission("svc.run"), Auth2Role("Runner")]
    public void Run() { }

    [Auth2Permission("Bad Key!")]
    public void Broken() { }
}

public class KeyRulesTests
{
    [Theory]
    [InlineData("orders.read", "orders.read")]
    [InlineData("  Orders.READ ", "orders.read")]
    [InlineData("a", "a")]
    [InlineData("has space", null)]
    [InlineData(".dot", null)]
    [InlineData("end-", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void PermissionKeys_FollowTheServerRule(string? input, string? expected) => Auth2Keys.NormalizePermissionKey(input).ShouldBe(expected);

    [Fact]
    public void PermissionKey_MaxLengthIs100()
    {
        Auth2Keys.NormalizePermissionKey(new string('a', 100)).ShouldNotBeNull();
        Auth2Keys.NormalizePermissionKey(new string('a', 101)).ShouldBeNull();
    }

    [Theory]
    [InlineData("Editor", "Editor")]
    [InlineData(" Blog Editor ", "Blog Editor")]
    [InlineData("bad/name", null)]
    [InlineData("", null)]
    public void RoleNames_FollowTheServerRule(string input, string? expected) => Auth2Keys.NormalizeRoleName(input).ShouldBe(expected);
}

public class ScannerTests
{
    private static EndpointDataSource Endpoints(params object[][] metadataPerEndpoint) =>
        new DefaultEndpointDataSource(metadataPerEndpoint.Select((m, i) =>
            (Endpoint)new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/e" + i), 0, new EndpointMetadataCollection(m), "e" + i)));

    private static PermissionScanner Permissions(ScanningPolicy policy, EndpointDataSource? endpoints = null, Action<Auth2PermissionOptions>? configure = null)
    {
        var o = new Auth2PermissionOptions { ScanningPolicy = policy, AssembliesToScan = [typeof(ScanFixturePage).Assembly] };
        configure?.Invoke(o);
        return new PermissionScanner([endpoints ?? Endpoints()], MsOptions.Create(o), NullLogger<PermissionScanner>.Instance);
    }

    [Fact]
    public void Manual_UsesOnlyTheList_Normalized_Sorted_WithoutInvalidKeys()
    {
        var scanner = Permissions(ScanningPolicy.Manual, Endpoints([new Auth2PermissionAttribute("from.endpoint")]),
            o => o.PermissionList = [new("Zeta.Key", "Zeta"), new("alpha.key"), new("bad key"), new("alpha.key", "dup")]);
        scanner.Scan().Select(p => p.Key).ShouldBe(["alpha.key", "zeta.key"]);
    }

    [Fact]
    public void AssemblyScan_ReadsEndpointMetadataAndAttributes_AndIgnoresTheList()
    {
        var endpoints = Endpoints([new Auth2PermissionAttribute("orders.read", "orders.write"), new Auth2RoleAttribute("Editor")], [new Auth2PermissionAttribute("orders.read")]);
        var scanner = Permissions(ScanningPolicy.AssemblyScan, endpoints, o => o.PermissionList = [new("listed.only")]);
        scanner.Scan().Select(p => p.Key).ShouldBe(["blazor.page", "orders.read", "orders.write", "svc.run"]);   // "Bad Key!" is skipped, the list is ignored
    }

    [Fact]
    public void Both_IsTheUnion_AndManualEntriesWin()
    {
        var endpoints = Endpoints([new Auth2PermissionAttribute("orders.read")]);
        var scanner = Permissions(ScanningPolicy.Both, endpoints, o => o.PermissionList = [new("orders.read", "Read orders", "Lets you read", "Orders"), new("manual.only")]);
        var result = scanner.Scan();
        result.Select(p => p.Key).ShouldBe(["blazor.page", "manual.only", "orders.read", "svc.run"]);
        result.Single(p => p.Key == "orders.read").DisplayName.ShouldBe("Read orders");
    }

    [Fact]
    public void RoleScanner_ReadsEndpointsAndAttributes_ManualWins_AndKeepsRolePermissions()
    {
        var endpoints = new[] { Endpoints([new Auth2RoleAttribute("Editor", "Writer")]) };
        var o = new Auth2RoleOptions
        {
            ScanningPolicy = ScanningPolicy.Both, AssembliesToScan = [typeof(ScanFixturePage).Assembly],
            RoleList = [new("editor", null, "Edits things", ["Orders.Read", "bad key"]), new("bad/name")],
        };
        var result = new RoleScanner(endpoints, MsOptions.Create(o), NullLogger<RoleScanner>.Instance).Scan();
        result.Select(r => r.Name).ShouldBe(["editor", "Runner", "Writer"]);   // "Editor" from the endpoint is replaced by the manual "editor"
        result[0].Description.ShouldBe("Edits things");
        result[0].Permissions.ShouldBe(["orders.read"]);
    }

    [Fact]
    public void NoEndpointDataSource_IsFine() =>
        new PermissionScanner([], MsOptions.Create(new Auth2PermissionOptions { ScanningPolicy = ScanningPolicy.AssemblyScan }), NullLogger<PermissionScanner>.Instance).Scan().ShouldBeEmpty();
}

public class CatalogSyncServiceTests
{
    private static (CatalogSyncService<string> Sync, List<IReadOnlyList<string>> Sent, Func<Result<SyncResponse>> Answer, FakeTimeProvider Time) Make(Func<IReadOnlyList<string>> items, int maxRetries = 3)
    {
        var sent = new List<IReadOnlyList<string>>();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var answer = new Func<Result<SyncResponse>>[] { () => Result<SyncResponse>.Success(new SyncResponse(1, 0, 0, ["x"])) };
        var sync = new CatalogSyncService<string>("things", items, (list, _) => { sent.Add(list); return Task.FromResult(answer[0]()); },
            MsOptions.Create(new Auth2ClientOptions { SyncMaxRetries = maxRetries }), time, NullLogger.Instance);
        return (sync, sent, () => answer[0](), time);
    }

    [Fact]
    public async Task UnchangedList_IsSentOnce_ChangedListAgain_EmptyListNever()
    {
        var current = new List<string> { "a", "b" };
        var (sync, sent, _, _) = Make(() => [.. current]);
        (await sync.PushAsync(default)).ShouldBeTrue();
        (await sync.PushAsync(default)).ShouldBeTrue();
        sent.Count.ShouldBe(1);
        current.Add("c");
        await sync.PushAsync(default);
        sent.Count.ShouldBe(2);

        var (empty, emptySent, _, _) = Make(() => []);
        (await empty.PushAsync(default)).ShouldBeTrue();
        emptySent.ShouldBeEmpty();
    }

    [Fact]
    public async Task TransientFailures_AreRetriedWithGrowingWaits_ThenItWorks()
    {
        var attempts = 0;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sync = new CatalogSyncService<string>("things", () => ["a"],
            (_, _) => Task.FromResult(++attempts < 3 ? Result<SyncResponse>.Failure(Auth2ClientErrorCodes.ServerUnavailable) : Result<SyncResponse>.Success(new SyncResponse(1, 0, 0, []))),
            MsOptions.Create(new Auth2ClientOptions { SyncMaxRetries = 5 }), time, NullLogger.Instance);
        var task = sync.PushAsync(default);
        for (var i = 0; i < 30 && !task.IsCompleted; i++) { time.Advance(TimeSpan.FromSeconds(2)); await Task.Delay(10); }
        (await task).ShouldBeTrue();
        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task ARefusal_IsNotRetried_AndGivingUpReturnsFalse()
    {
        var attempts = 0;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var refused = new CatalogSyncService<string>("things", () => ["a"], (_, _) => { attempts++; return Task.FromResult(Result<SyncResponse>.Failure(Auth2ErrorCodes.SyncNotAllowed)); },
            MsOptions.Create(new Auth2ClientOptions()), time, NullLogger.Instance);
        (await refused.PushAsync(default)).ShouldBeFalse();
        attempts.ShouldBe(1);

        var down = 0;
        var giveUp = new CatalogSyncService<string>("things", () => ["a"], (_, _) => { down++; return Task.FromResult(Result<SyncResponse>.Failure(Auth2ClientErrorCodes.ServerUnavailable)); },
            MsOptions.Create(new Auth2ClientOptions { SyncMaxRetries = 2 }), time, NullLogger.Instance);
        var task = giveUp.PushAsync(default);
        for (var i = 0; i < 60 && !task.IsCompleted; i++) { time.Advance(TimeSpan.FromSeconds(10)); await Task.Delay(5); }
        (await task).ShouldBeFalse();
        down.ShouldBe(3);   // the first try and two retries
    }

    [Fact]
    public async Task AFailedSync_IsSentAgainNextTime_BecauseTheHashIsOnlyKeptOnSuccess()
    {
        var fail = true;
        var sent = 0;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sync = new CatalogSyncService<string>("things", () => ["a"], (_, _) => { sent++; return Task.FromResult(fail ? Result<SyncResponse>.Failure(Auth2ErrorCodes.SyncNotAllowed) : Result<SyncResponse>.Success(new SyncResponse(1, 0, 0, []))); },
            MsOptions.Create(new Auth2ClientOptions()), time, NullLogger.Instance);
        await sync.PushAsync(default);
        fail = false;
        (await sync.PushAsync(default)).ShouldBeTrue();
        sent.ShouldBe(2);
    }
}

public class SyncHostedServiceTests
{
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
        public void Start() => _started.Cancel();
    }

    private static (Auth2SyncHostedService Service, IAuth2ApiClientService Api, Auth2ClientState State, FakeLifetime Lifetime, Auth2Catalog Catalog, FakeTimeProvider Time) Make(
        PermissionSyncPolicy p = PermissionSyncPolicy.Push, RoleSyncPolicy r = RoleSyncPolicy.Push, GroupSyncPolicy g = GroupSyncPolicy.None, Action<Auth2ClientOptions>? client = null)
    {
        var api = Substitute.For<IAuth2ApiClientService>();
        api.SyncPermissionsAsync(Arg.Any<PermissionSyncRequest>(), Arg.Any<CancellationToken>()).Returns(Result<SyncResponse>.Success(new SyncResponse(1, 0, 0, [])));
        api.SyncRolesAsync(Arg.Any<RoleSyncRequest>(), Arg.Any<CancellationToken>()).Returns(Result<SyncResponse>.Success(new SyncResponse(1, 0, 0, [])));
        api.GetPermissionsAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<PermissionDto>>.Success([new PermissionDto("a.b", null, null, null, false)]));
        api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<RoleDto>>.Success([new RoleDto("Editor", null, false, false, ["a.b"])]));
        api.GetGroupsAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<GroupDto>>.Success([new GroupDto("finance", null, null, [], [])]));

        var permissionScanner = Substitute.For<IPermissionScanner>();
        permissionScanner.Scan().Returns([new PermissionDefinition("a.b")]);
        var roleScanner = Substitute.For<IRoleScanner>();
        roleScanner.Scan().Returns([new RoleDefinition("Editor", null, null, ["a.b"])]);

        var state = new Auth2ClientState();
        var lifetime = new FakeLifetime();
        var catalog = new Auth2Catalog();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = new Auth2ClientOptions { CatalogRefreshInterval = TimeSpan.FromMinutes(5) };
        client?.Invoke(options);
        var service = new Auth2SyncHostedService(lifetime, state, permissionScanner, roleScanner, api, MsOptions.Create(options),
            MsOptions.Create(new Auth2PermissionOptions { SyncPolicy = p }), MsOptions.Create(new Auth2RoleOptions { SyncPolicy = r }),
            MsOptions.Create(new Auth2GroupOptions { SyncPolicy = g }), catalog, time, NullLoggerFactory.Instance);
        return (service, api, state, lifetime, catalog, time);
    }

    private static async Task SettleAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10);
    }

    [Fact]
    public async Task Waits_ForTheApplicationToStart_AndForAGoodHandshake_ThenPushesPermissionsBeforeRoles()
    {
        var (service, api, state, lifetime, _, _) = Make();
        await service.StartAsync(default);
        await Task.Delay(100);
        await api.DidNotReceive().SyncPermissionsAsync(Arg.Any<PermissionSyncRequest>(), Arg.Any<CancellationToken>());

        lifetime.Start();                      // endpoints are mapped now
        await Task.Delay(100);
        await api.DidNotReceive().SyncPermissionsAsync(Arg.Any<PermissionSyncRequest>(), Arg.Any<CancellationToken>());   // still no handshake

        state.Set(HandshakeStatus.Ok);
        await SettleAsync(() => api.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IAuth2ApiClientService.SyncRolesAsync)));
        Received.InOrder(() =>
        {
            api.SyncPermissionsAsync(Arg.Any<PermissionSyncRequest>(), Arg.Any<CancellationToken>());
            api.SyncRolesAsync(Arg.Any<RoleSyncRequest>(), Arg.Any<CancellationToken>());
        });
        await service.StopAsync(default);
    }

    [Fact]
    public async Task PolicyNone_DoesNothing()
    {
        var (service, api, state, lifetime, _, _) = Make(PermissionSyncPolicy.None, RoleSyncPolicy.None);
        lifetime.Start(); state.Set(HandshakeStatus.Ok);
        await service.StartAsync(default);
        await Task.Delay(150);
        api.ReceivedCalls().ShouldBeEmpty();
        await service.StopAsync(default);
    }

    [Fact]
    public async Task Pull_FillsTheCatalog_AndReadsAgainAfterTheInterval()
    {
        var (service, api, state, lifetime, catalog, time) = Make(PermissionSyncPolicy.Pull, RoleSyncPolicy.Pull, GroupSyncPolicy.Pull);
        lifetime.Start(); state.Set(HandshakeStatus.Ok);
        await service.StartAsync(default);
        await SettleAsync(() => catalog.LastReadUtc is not null);
        catalog.Permissions.Single().Key.ShouldBe("a.b");
        catalog.Roles.Single().Permissions.ShouldBe(["a.b"]);
        catalog.Groups.Single().Name.ShouldBe("finance");
        await api.DidNotReceive().SyncPermissionsAsync(Arg.Any<PermissionSyncRequest>(), Arg.Any<CancellationToken>());   // pull only

        var calls = api.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAuth2ApiClientService.GetPermissionsAsync));
        for (var i = 0; i < 20; i++) { time.Advance(TimeSpan.FromMinutes(1)); await Task.Delay(10); }
        api.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAuth2ApiClientService.GetPermissionsAsync)).ShouldBeGreaterThan(calls);
        await service.StopAsync(default);
    }

    [Fact]
    public async Task Both_PushesFirst_ThenPulls()
    {
        var (service, api, state, lifetime, catalog, _) = Make(PermissionSyncPolicy.Both, RoleSyncPolicy.None);
        lifetime.Start(); state.Set(HandshakeStatus.Ok);
        await service.StartAsync(default);
        await SettleAsync(() => catalog.LastReadUtc is not null);
        Received.InOrder(() =>
        {
            api.SyncPermissionsAsync(Arg.Any<PermissionSyncRequest>(), Arg.Any<CancellationToken>());
            api.GetPermissionsAsync(Arg.Any<CancellationToken>());
        });
        await service.StopAsync(default);
    }

    [Fact]
    public async Task AFailedSync_IsLoggedAndTheApplicationCarriesOn_ButFailOnSyncErrorStopsIt()
    {
        var (quiet, api, state, lifetime, _, _) = Make();
        api.SyncPermissionsAsync(Arg.Any<PermissionSyncRequest>(), Arg.Any<CancellationToken>()).Returns(Result<SyncResponse>.Failure(Auth2ErrorCodes.SyncNotAllowed));
        lifetime.Start(); state.Set(HandshakeStatus.Ok);
        await quiet.StartAsync(default);
        await Task.Delay(150);
        quiet.ExecuteTask!.IsFaulted.ShouldBeFalse();
        await quiet.StopAsync(default);

        var (strict, api2, state2, lifetime2, _, _) = Make(client: o => o.FailOnSyncError = true);
        api2.SyncPermissionsAsync(Arg.Any<PermissionSyncRequest>(), Arg.Any<CancellationToken>()).Returns(Result<SyncResponse>.Failure(Auth2ErrorCodes.SyncNotAllowed));
        lifetime2.Start(); state2.Set(HandshakeStatus.Ok);
        await strict.StartAsync(default);
        await SettleAsync(() => strict.ExecuteTask!.IsFaulted);
        strict.ExecuteTask!.IsFaulted.ShouldBeTrue();
    }
}
