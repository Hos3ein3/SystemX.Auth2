using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Tests;

public class OptionsValidationTests
{
    private static Auth2ClientOptions Good() => new() { ServerUrl = "https://auth.test", ApplicationName = "Blog", ClientId = "blog", ClientSecret = "s" };
    private static ValidateOptionsResult Check(Auth2ClientOptions o) => new Auth2ClientOptionsValidator().Validate(null, o);

    [Fact]
    public void Good_Options_Pass_AndProjectKeyIsOptional() => Check(Good()).Succeeded.ShouldBeTrue();

    [Fact]
    public void MissingValues_AreAllReported_WithClearMessages()
    {
        var r = Check(new Auth2ClientOptions());
        r.Failed.ShouldBeTrue();
        r.Failures!.Count().ShouldBe(4);
        string.Join("\n", r.Failures!).ShouldContain("ServerUrl");
        string.Join("\n", r.Failures!).ShouldContain("ClientSecret");
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://auth.test")]
    [InlineData("/relative")]
    public void ServerUrl_MustBeAbsoluteHttp(string url)
    {
        var o = Good(); o.ServerUrl = url;
        Check(o).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("blog", true)]
    [InlineData("paper-trade_2", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("Blog", false)]
    [InlineData("has space", false)]
    [InlineData("_global", false)]
    [InlineData("", false)]
    public void ProjectKey_FollowsTheServerRule(string key, bool valid)
    {
        var o = Good(); o.ProjectKey = key;
        Check(o).Succeeded.ShouldBe(valid);
        Auth2ClientOptionsValidator.IsValidProjectKey(key).ShouldBe(valid);
    }

    [Fact]
    public void Timeouts_AndRetryNumbers_AreChecked()
    {
        var o = Good(); o.HandshakeTimeout = TimeSpan.Zero; o.HandshakeMaxRetries = -1; o.HandshakeMaxDelay = TimeSpan.FromMilliseconds(1);
        Check(o).Failures!.Count().ShouldBe(3);
    }

    [Fact]
    public async Task InvalidOptions_StopTheHost_AtStartup()
    {
        using var host = new HostBuilder().ConfigureServices(s => s.AddAuth2Client("https://auth.test", o => o.ApplicationName = "")).Build();
        var ex = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());
        ex.Message.ShouldContain("ApplicationName");
    }
}

public class BuilderTests
{
    private static Auth2ClientOptions Options(ServiceProvider sp) => sp.GetRequiredService<IOptions<Auth2ClientOptions>>().Value;

    [Fact]
    public void TheOrderOfTheFeatureCalls_DoesNotMatter()
    {
        void A(Microsoft.Extensions.DependencyInjection.IServiceCollection s) => s.AddAuth2Client("https://auth.test", o => { o.ApplicationName = "Blog"; o.ClientId = "blog"; o.ClientSecret = "x"; })
            .ServerWithMultiProject("blog").EnablePermissions(o => o.SyncPolicy = PermissionSyncPolicy.Push).EnableRoles(o => o.SyncPolicy = RoleSyncPolicy.Push)
            .EnableGroups(o => o.SyncPolicy = GroupSyncPolicy.Pull).EnableMachineToMachine(o => o.Scopes = ["a.read"]).EnableResilience().EnableHealthCheck();
        void B(Microsoft.Extensions.DependencyInjection.IServiceCollection s) => s.AddAuth2Client("https://auth.test", o => { o.ApplicationName = "Blog"; o.ClientId = "blog"; o.ClientSecret = "x"; })
            .EnableHealthCheck().EnableResilience().EnableMachineToMachine(o => o.Scopes = ["a.read"]).EnableGroups(o => o.SyncPolicy = GroupSyncPolicy.Pull)
            .EnableRoles(o => o.SyncPolicy = RoleSyncPolicy.Push).EnablePermissions(o => o.SyncPolicy = PermissionSyncPolicy.Push).ServerWithMultiProject("blog");

        foreach (var setup in new[] { A, B })
        {
            var services = new ServiceCollection();
            services.AddLogging();
            setup(services);
            using var sp = services.BuildServiceProvider();
            Options(sp).ProjectKey.ShouldBe("blog");
            sp.GetRequiredService<IOptions<Auth2PermissionOptions>>().Value.SyncPolicy.ShouldBe(PermissionSyncPolicy.Push);
            sp.GetRequiredService<IOptions<Auth2RoleOptions>>().Value.SyncPolicy.ShouldBe(RoleSyncPolicy.Push);
            sp.GetRequiredService<IOptions<Auth2GroupOptions>>().Value.SyncPolicy.ShouldBe(GroupSyncPolicy.Pull);
            sp.GetRequiredService<IOptions<Auth2MachineOptions>>().Value.Scopes.ShouldBe(["a.read"]);
        }
    }

    [Fact]
    public void WithoutServerWithMultiProject_NoProjectKeyIsSent()
    {
        var services = new ServiceCollection();
        services.AddAuth2Client("https://auth.test", o => { o.ApplicationName = "Blog"; o.ClientId = "blog"; o.ClientSecret = "x"; });
        using var sp = services.BuildServiceProvider();
        Options(sp).ProjectKey.ShouldBeNull();
    }

    [Fact]
    public void ConfigurationSection_IsSupported()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth2:ServerUrl"] = "https://auth.test", ["Auth2:ApplicationName"] = "Blog", ["Auth2:ClientId"] = "blog", ["Auth2:ClientSecret"] = "x",
            ["Auth2:ProjectKey"] = "blog", ["Auth2:OnServerUnavailable"] = "Throw", ["Auth2:HandshakeTimeout"] = "00:00:09",
        }).Build();
        var services = new ServiceCollection();
        services.AddAuth2Client(config.GetSection("Auth2"));
        using var sp = services.BuildServiceProvider();
        var o = Options(sp);
        (o.ServerUrl, o.ProjectKey, o.OnServerUnavailable, o.HandshakeTimeout).ShouldBe(("https://auth.test", "blog", StartupFailureBehavior.Throw, TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void TheClientHasNoSingleProjectMethod()
    {
        typeof(Microsoft.Extensions.DependencyInjection.Auth2ClientServiceCollectionExtensions).GetMethods()
            .ShouldNotContain(m => m.Name == "ServerWithSingleProject");
    }
}
