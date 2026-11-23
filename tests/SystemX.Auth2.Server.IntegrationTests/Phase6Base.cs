using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>Shared helpers for the account tests: two applications, client tokens, user tokens, JSON calls.</summary>
public abstract class Phase6Base : IdentityTestBase
{
    protected const string Redirect = "https://client.test/cb";

    /// <summary>Two applications. Both allow the password grant and have the same registered redirect URI.</summary>
    protected async Task PrepareAppsAsync(bool selfUi = true, Action<ApplicationSettings>? both = null)
    {
        await SetUpAsync();
        foreach (var app in new[] { A, B })
            await UpdateSettingsAsync(app, s =>
            {
                s.Clients.AllowSelfUiLogin = selfUi;
                s.Redirects.AllowedRedirectUris.Add(Redirect);
                both?.Invoke(s);
            });
    }

    protected async Task<string> ClientTokenAsync(string clientId, string scope, Browser? b = null)
    {
        var r = await (b ?? new Browser(Factory)).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientId == "appa" ? ASecret : BSecret, ["scope"] = scope,
        });
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await r.JsonAsync()).Str("access_token");
    }

    protected async Task<JsonElement> UserTokensAsync(string login, string password, string clientId = "appa", string scope = "openid offline_access auth2.me")
    {
        var r = await new Browser(Factory).PostFormAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = login, ["password"] = password, ["scope"] = scope,
            ["client_id"] = clientId, ["client_secret"] = clientId == "appa" ? ASecret : BSecret,
        });
        var body = await r.JsonAsync();
        r.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        return body;
    }

    protected async Task<(HttpStatusCode Status, JsonElement Body)> CallAsync(Browser b, string url, object? body, string? bearer, string? lang = null)
    {
        var r = await b.PostJsonAsync(url, body, bearer, lang);
        var text = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    protected async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await db.AuditEvents.Where(a => a.Action == action).OrderBy(a => a.OccurredAtUtc).ToListAsync();
    }

    protected async Task<(int Users, int Rows)> CountsAsync(string email)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var ids = await db.Users.Where(u => u.Email == email).Select(u => u.Id).ToListAsync();
        return (ids.Count, await db.UserApplications.CountAsync(x => ids.Contains(x.UserId)));
    }

    protected async Task<bool> HasRowAsync(string email, Auth2Application app)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await db.UserApplications.AnyAsync(x => x.ApplicationId == app.Id && db.Users.Any(u => u.Id == x.UserId && u.Email == email));
    }

    protected async Task<AppUser> UserAsync(string email)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
    }

    protected async Task ConfirmEmailAsync(string email)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.Users.SingleAsync(u => u.Email == email)).EmailConfirmed = true;
        await db.SaveChangesAsync();
    }

    protected static string ErrorCode(JsonElement body) => body.Str("code");
}
