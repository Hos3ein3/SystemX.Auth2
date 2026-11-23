using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Server.Persistence.Seeding;

namespace SystemX.Auth2.Server.Tests;

public class ApplicationScopeTests
{
    [Fact]
    public void Begin_ActivatesGlobalScope_AndDisposeEndsIt()
    {
        var scope = new ApplicationScope();
        scope.IsGlobalActive.ShouldBeFalse();
        var d = scope.Begin();
        scope.IsGlobalActive.ShouldBeTrue();
        d.Dispose();
        scope.IsGlobalActive.ShouldBeFalse();
    }

    [Fact]
    public void Begin_Nested_StaysActiveUntilAllDisposed()
    {
        var scope = new ApplicationScope();
        var a = scope.Begin();
        var b = scope.Begin();
        a.Dispose();
        a.Dispose(); // double dispose must not release the second scope
        scope.IsGlobalActive.ShouldBeTrue();
        b.Dispose();
        scope.IsGlobalActive.ShouldBeFalse();
    }

    [Fact]
    public void Set_StoresApplicationId()
    {
        var scope = new ApplicationScope();
        var id = Guid.NewGuid();
        scope.Set(id);
        scope.ApplicationId.ShouldBe(id);
    }
}

public class ServerModeRegistrationTests
{
    private static IServiceCollection Base(Action<IServiceCollection>? after = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Auth2"] = "Host=localhost;Database=x",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSystemXAuth2Server(config);
        after?.Invoke(services);
        return services;
    }

    [Fact]
    public void Default_IsMultiApplication()
    {
        using var sp = Base().BuildServiceProvider();
        sp.GetRequiredService<IOptions<Auth2ServerOptions>>().Value.Mode.ShouldBe(ServerMode.MultiApplication);
    }

    [Fact]
    public void ServerWithSingleApplication_SetsModeAndSharedCredentials()
    {
        using var sp = Base(s => s.ServerWithSingleApplication()).BuildServiceProvider();
        var o = sp.GetRequiredService<IOptions<Auth2ServerOptions>>().Value;
        o.Mode.ShouldBe(ServerMode.SingleApplication);
        o.CredentialScope.ShouldBe(CredentialScope.Shared);
    }

    [Fact]
    public void ServerWithSingleApplication_RegistersSingleApplicationContext()
    {
        var services = Base(s => s.ServerWithSingleApplication());
        services.Last(d => d.ServiceType == typeof(IApplicationContext)).ImplementationType.ShouldBe(typeof(SingleApplicationContext));
    }

    [Fact]
    public void MissingConnectionString_Throws() =>
        Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddSystemXAuth2Server(new ConfigurationBuilder().Build()));
}

public class SnakeCaseTests
{
    [Theory]
    [InlineData("ApplicationId", "application_id")]
    [InlineData("OpenIddictApplications", "open_iddict_applications")]
    [InlineData("UserAccessVersion", "user_access_version")]
    [InlineData("PK_Users", "pk_users")]
    [InlineData("IX_roles_ApplicationId_NormalizedName", "ix_roles_application_id_normalized_name")]
    [InlineData("already_snake", "already_snake")]
    public void Snake_Converts(string input, string expected) => Auth2DbContext.Snake(input).ShouldBe(expected);
}

public class SeederHelperTests
{
    [Theory]
    [InlineData("Default", "default")]
    [InlineData("My App!", "my-app")]
    [InlineData("A", "default")]
    [InlineData("  Paper Trade ", "paper-trade")]
    public void SlugFor_MakesValidKey(string name, string expected)
    {
        var slug = Auth2Seeder.SlugFor(name);
        slug.ShouldBe(expected);
        SystemX.Auth2.Server.Domain.Settings.ProjectKeyRules.IsValidForNewApplication(slug).ShouldBeTrue();
    }
}
