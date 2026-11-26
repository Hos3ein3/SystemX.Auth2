using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>Turning two-factor off on <c>/Manage/TwoFactor/Enable</c> with one of the recovery codes that the page showed.</summary>
public class RecoveryCodeDisableTests : Phase6Base
{
    private const string Disable = "/Manage/TwoFactor/Enable?handler=Disable";

    private async Task<(Browser Browser, List<string> Codes)> EnabledAsync()
    {
        await PrepareAppsAsync(selfUi: true);
        var user = await CreateUserInAsync("appa");
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<SystemX.Auth2.Server.Application.IGlobalScope>().Begin();
            if (!await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == A.Id)) await JoinAsync(user, A);
        }
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await b.GetAsync("/Manage/TwoFactor/Enable");

        string key;
        await using (var scope = ScopeFor("appa"))
        {
            var m = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            key = (await m.GetAuthenticatorKeyAsync((await m.FindByNameAsync("sara"))!))!;
        }
        var enabled = await b.PostPageAsync("/Manage/TwoFactor/Enable", new Dictionary<string, string> { ["Input.Code"] = Totp.Now(key) });
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await enabled.Content.ReadAsStringAsync();
        var codes = Regex.Matches(html, @"select-all"">\s*([^<\s]+)\s*<").Select(m => m.Groups[1].Value).ToList();   // the codes, as the page shows them
        codes.Count.ShouldBeGreaterThan(1, "the page shows the recovery codes");
        return (b, codes);
    }

    private async Task<bool> TwoFactorOnAsync()
    {
        await using var scope = ScopeFor("appa");
        var m = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await m.GetTwoFactorEnabledAsync((await m.FindByNameAsync("sara"))!);
    }

    [Fact]
    public async Task ACodeAsShown_TurnsTwoFactorOff()
    {
        var (b, codes) = await EnabledAsync();
        var r = await b.PostPageAsync(Disable, new Dictionary<string, string> { ["Input.Password"] = PassA, ["Input.Code"] = codes[0] });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect, await r.Content.ReadAsStringAsync());
        (await TwoFactorOnAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task SigningInWithARecoveryCode_OnTheSecondStep_LandsOnTheAccountPage_NotOnNotFound()
    {
        var (_, codes) = await EnabledAsync();
        var b = new Browser(Factory);
        var login = await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA });
        login.Location().ShouldStartWith("/Account/TwoFactor");
        var page = await (await b.GetAsync(login.Location()!)).Content.ReadAsStringAsync();
        var token = Regex.Match(page, "name=\"TwoFactorToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        var second = await b.PostPageAsync(login.Location()!, new Dictionary<string, string> { ["TwoFactorToken"] = token, ["Input.Code"] = "", ["Input.RecoveryCode"] = codes[0] });
        second.StatusCode.ShouldBe(HttpStatusCode.Redirect, await second.Content.ReadAsStringAsync());
        var url = second.Location()!;
        string? last = null;
        for (var hop = 0; hop < 6; hop++)
        {
            var next = await b.GetAsync(url);
            ((int)next.StatusCode).ShouldNotBe(404, url);
            last = url;
            if (next.Headers.Location is null) break;
            url = next.Headers.Location.ToString();
        }
        last.ShouldNotBeNull().ShouldStartWith("/Manage");   // an ordinary person ends on their own account page
    }

    [Theory]
    [InlineData("lower")]
    [InlineData("spaces")]
    [InlineData("nohyphen")]
    public async Task ACodeTypedLoosely_TurnsTwoFactorOff(string how)
    {
        var (b, codes) = await EnabledAsync();
        var typed = how switch { "lower" => codes[0].ToLowerInvariant(), "spaces" => $"  {codes[0]}  ", _ => codes[0].Replace("-", "") };
        var r = await b.PostPageAsync(Disable, new Dictionary<string, string> { ["Input.Password"] = PassA, ["Input.Code"] = typed });
        r.StatusCode.ShouldBe(HttpStatusCode.Redirect, await r.Content.ReadAsStringAsync());
        (await TwoFactorOnAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task AWrongCode_OrAWrongPassword_DoesNotTurnItOff_AndDoesNotBurnTheCode()
    {
        var (b, codes) = await EnabledAsync();
        (await b.PostPageAsync(Disable, new Dictionary<string, string> { ["Input.Password"] = PassA, ["Input.Code"] = "AAAAA-BBBBB" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await b.PostPageAsync(Disable, new Dictionary<string, string> { ["Input.Password"] = "Wrong-Password-1", ["Input.Code"] = codes[0] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TwoFactorOnAsync()).ShouldBeTrue();
        (await b.PostPageAsync(Disable, new Dictionary<string, string> { ["Input.Password"] = PassA, ["Input.Code"] = codes[0] })).StatusCode.ShouldBe(HttpStatusCode.Redirect);   // the code still works
        (await TwoFactorOnAsync()).ShouldBeFalse();
    }
}

/// <summary>The same with one credential row per person (<c>CredentialScope.Shared</c>).</summary>
public class RecoveryCodeDisableSharedTests : RecoveryCodeDisableTests
{
    protected override void Customize(IServiceCollection services) =>
        services.Configure<SystemX.Auth2.Server.Application.Auth2ServerOptions>(o => o.CredentialScope = CredentialScope.Shared);
}
