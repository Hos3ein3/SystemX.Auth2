using System.Net;
using System.Text.RegularExpressions;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The Tailwind build, the strict content security policy and the rule "no inline script or style" for the server-rendered pages.</summary>
public class UiPipelineTests : Phase6Base
{
    private static string ServerProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SystemX.Auth2.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "SystemX.Auth2.Server");
    }

    [Fact]
    public async Task TheTailwindBuildProducesTheStylesheet_WithTheThemeVariablesAndTheClassesOfThePages()
    {
        await SetUpAsync();
        var r = await new Browser(Factory).GetAsync("/css/site.css");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.ShouldBe("text/css");
        var css = await r.Content.ReadAsStringAsync();
        css.Length.ShouldBeGreaterThan(5_000);
        css.ShouldContain("--a2-color-primary");          // the runtime variables of the branding
        css.ShouldContain(".bg-a2-primary");              // the utility that points to the variable (a color change needs no rebuild)
        // a class that the pages and components use is in the file (the sources are scanned)
        var used = Directory.EnumerateFiles(Path.Combine(ServerProjectDirectory(), "Pages"), "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"class=""([^""@]*)""").Select(m => m.Groups[1].Value))
            .SelectMany(v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(c => c is "flex" or "rounded" or "border").Distinct().ToList();
        used.ShouldNotBeEmpty();
        css.ShouldContain(".flex");
    }

    [Fact]
    public async Task TheContentSecurityPolicy_IsStrict_AndTheNonceOfTheAnswerIsTheOneOnTheStyleBlock()
    {
        await PrepareAppsAsync();
        var r = await new Browser(Factory).GetAsync("/Account/Login?ClientId=appa");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csp = r.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldNotContain("unsafe-inline");
        csp.ShouldNotContain("unsafe-eval");
        csp.ShouldNotContain("cdn.");
        csp.ShouldContain("script-src 'self'");
        csp.ShouldContain("frame-ancestors 'none'");
        var nonce = Regex.Match(csp, "'nonce-([^']+)'").Groups[1].Value;
        nonce.ShouldNotBeNullOrEmpty();
        var html = await r.Content.ReadAsStringAsync();
        html.ShouldContain($"<style nonce=\"{nonce}\"");

        var second = await new Browser(Factory).GetAsync("/Account/Login?ClientId=appa");
        Regex.Match(second.Headers.GetValues("Content-Security-Policy").Single(), "'nonce-([^']+)'").Groups[1].Value.ShouldNotBe(nonce);   // new for every request
    }

    [Fact]
    public void NoPage_HasAnInlineEventHandler_AnInlineStyleAttribute_OrAnInlineScript()
    {
        var root = Path.Combine(ServerProjectDirectory(), "Pages");
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (Regex.IsMatch(text, @"\son(click|change|submit|input|load|error|focus|blur)\s*=", RegexOptions.IgnoreCase)) problems.Add(Path.GetRelativePath(root, file) + ": inline event handler");
            if (Regex.IsMatch(text, @"\sstyle\s*=\s*""")) problems.Add(Path.GetRelativePath(root, file) + ": inline style attribute");
            if (Regex.IsMatch(text, @"<script(?![^>]*\ssrc=)", RegexOptions.IgnoreCase)) problems.Add(Path.GetRelativePath(root, file) + ": inline script");
            if (text.Contains("cdn.tailwindcss.com")) problems.Add(Path.GetRelativePath(root, file) + ": Tailwind CDN");
        }
        problems.ShouldBeEmpty();
    }
}
