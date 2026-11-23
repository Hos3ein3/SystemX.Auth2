using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Localization;

namespace SystemX.Auth2.Server.IntegrationTests;

public class LocalizationIntegrationTests : IdentityTestBase
{
    private async Task SetUpAllCulturesAsync()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s => s.Localization.SupportedCultures = ["en", "fa", "nl", "it"]);
    }

    private async Task<string?> DetermineAsync(Action<HttpRequest> build)
    {
        await using var scope = NewScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        build(ctx.Request);
        var result = await new Auth2RequestCultureProvider().DetermineProviderCultureResult(ctx);
        return result?.Cultures.First().Value;
    }

    [Fact]
    public async Task Culture_UiLocalesParameter_IsUsed()
    {
        await SetUpAllCulturesAsync();
        (await DetermineAsync(r => r.QueryString = new QueryString("?client_id=appa&ui_locales=nl"))).ShouldBe("nl");
    }

    [Fact]
    public async Task Culture_UiLocalesInsideReturnUrl_IsUsed()
    {
        await SetUpAllCulturesAsync();
        var returnUrl = Uri.EscapeDataString("/connect/authorize?client_id=appa&ui_locales=it");
        (await DetermineAsync(r => r.QueryString = new QueryString("?ReturnUrl=" + returnUrl))).ShouldBe("it");
    }

    [Fact]
    public async Task Culture_Cookie_IsUsed_WhenNoUiLocales()
    {
        await SetUpAllCulturesAsync();
        (await DetermineAsync(r => { r.QueryString = new QueryString("?client_id=appa"); r.Headers.Cookie = $"{Auth2RequestCultureProvider.CookieName}=fa"; })).ShouldBe("fa");
    }

    [Fact]
    public async Task Culture_AcceptLanguage_IsUsed_WhenNothingElse()
    {
        await SetUpAllCulturesAsync();
        (await DetermineAsync(r => { r.QueryString = new QueryString("?client_id=appa"); r.Headers.AcceptLanguage = "it-IT,it;q=0.9,en;q=0.5"; })).ShouldBe("it");
    }

    [Fact]
    public async Task Culture_ApplicationDefault_IsUsedLast()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s => { s.Localization.SupportedCultures = ["en", "fa"]; s.Localization.DefaultCulture = "fa"; });
        (await DetermineAsync(r => r.QueryString = new QueryString("?client_id=appa"))).ShouldBe("fa");
    }

    [Fact]
    public async Task Culture_NotSupportedByApplication_IsSkipped()
    {
        await SetUpAsync();
        await UpdateSettingsAsync(A, s => s.Localization.SupportedCultures = ["en", "fa"]);
        (await DetermineAsync(r => { r.QueryString = new QueryString("?client_id=appa&ui_locales=nl"); r.Headers.AcceptLanguage = "fa"; })).ShouldBe("fa");
    }

    [Fact]
    public async Task Culture_UnknownClient_UsesServerDefaults()
    {
        await SetUpAllCulturesAsync();
        (await DetermineAsync(r => { r.QueryString = new QueryString("?client_id=nope&ui_locales=nl"); })).ShouldBe("nl");
        (await DetermineAsync(_ => { })).ShouldBe("en");
    }
}
