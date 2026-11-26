using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SystemX.Auth2.Server.Tests;

/// <summary>Razor runtime compilation (a change of a <c>.cshtml</c> shows without a build) is for development: on by the environment, and a setting can change it.</summary>
public class RazorRuntimeCompilationTests
{
    private static bool On(params (string Key, string Value)[] values) =>
        Auth2ServerServiceCollectionExtensions.UseRazorRuntimeCompilation(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build());

    [Theory]
    [InlineData("Development", true)]
    [InlineData("development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Testing", false)]
    public void TheEnvironmentDecides_ByDefault(string environment, bool expected) =>
        On(("ASPNETCORE_ENVIRONMENT", environment)).ShouldBe(expected);

    [Fact]
    public void WithoutAnyEnvironment_ItIsProduction_SoItIsOff()
    {
        On().ShouldBeFalse();
        On(("DOTNET_ENVIRONMENT", "Development")).ShouldBeTrue();   // the other name of the variable
    }

    [Theory]
    [InlineData("Development", "false", false)]   // switched off even in development
    [InlineData("Production", "true", true)]      // switched on by hand
    [InlineData("Development", "true", true)]
    public void TheSetting_Overrides(string environment, string setting, bool expected) =>
        On(("ASPNETCORE_ENVIRONMENT", environment), ("Auth2:RazorRuntimeCompilation", setting)).ShouldBe(expected);
}
