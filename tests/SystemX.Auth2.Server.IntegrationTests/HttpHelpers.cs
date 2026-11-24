using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>A tiny browser: keeps cookies, never follows redirects, and can post to a Razor Page with an anti-forgery token (the pages have no HTML).</summary>
public sealed class Browser
{
    private readonly HttpClient _client;
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, string> _cookies = [];

    public Browser(Auth2ServerFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        _services = factory.Services;
    }

    /// <summary>True when the cookie exists. The session cookie has the project key in its name, so the plain name matches any project.</summary>
    public bool HasCookie(string name) => _cookies.Keys.Any(k => k == name || k.StartsWith(name + ".", StringComparison.Ordinal));

    /// <summary>Names of all cookies the browser holds.</summary>
    public IReadOnlyCollection<string> CookieNames => _cookies.Keys;

    /// <summary>The session cookie belongs to one project. A request built outside the server needs the same project to read it.</summary>
    private void BindProject(IServiceProvider services)
    {
        var name = _cookies.Keys.FirstOrDefault(k => k.StartsWith(SystemX.Auth2.Server.Infrastructure.Auth.Auth2Cookie.CookieName + ".", StringComparison.Ordinal));
        if (name is not null)
            services.GetRequiredService<SystemX.Auth2.Server.Application.IProjectScope>().Set(Guid.Empty, name[(SystemX.Auth2.Server.Infrastructure.Auth.Auth2Cookie.CookieName.Length + 1)..]);
    }

    public async Task<HttpResponseMessage> GetAsync(string url, string? bearer = null) => await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), bearer);

    public async Task<HttpResponseMessage> PostFormAsync(string url, IDictionary<string, string> form, string? bearer = null) =>
        await SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) }, bearer);

    public async Task<HttpResponseMessage> PostJsonAsync(string url, object? body, string? bearer = null, string? acceptLanguage = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        if (acceptLanguage is not null) request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        return await SendAsync(request, bearer);
    }

    /// <summary>Any method with an optional JSON body and extra headers.</summary>
    public async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, object? body, string? bearer, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is string raw) request.Content = new StringContent(raw, Encoding.UTF8, "application/json");
        else if (body is not null) request.Content = JsonContent.Create(body);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        return await SendAsync(request, bearer);
    }

    /// <summary>Posts to a Razor Page handler. Adds a valid anti-forgery cookie and token.</summary>
    public async Task<HttpResponseMessage> PostPageAsync(string url, IDictionary<string, string> form)
    {
        using var scope = _services.CreateScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        BindProject(scope.ServiceProvider);
        // An anti-forgery token is bound to the signed-in user, so build it as the user of the session cookie (if there is one).
        if (_cookies.Count > 0) ctx.Request.Headers.Cookie = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
        var session = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.AuthenticateAsync(ctx, SystemX.Auth2.Server.Infrastructure.Auth.Auth2Cookie.Scheme);
        if (session.Succeeded) ctx.User = session.Principal!;
        var tokens = scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx);
        foreach (var header in ctx.Response.Headers.SetCookie) Remember(header!);
        var fields = new Dictionary<string, string>(form) { [tokens.FormFieldName] = tokens.RequestToken! };
        return await PostFormAsync(url, fields);
    }

    /// <summary>A JSON request of a signed-in user with a valid anti-forgery header (like the script of the UI sends).</summary>
    public async Task<HttpResponseMessage> SendJsonWithTokenAsync(HttpMethod method, string url, object? body)
    {
        using var scope = _services.CreateScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        BindProject(scope.ServiceProvider);
        if (_cookies.Count > 0) ctx.Request.Headers.Cookie = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
        var session = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.AuthenticateAsync(ctx, SystemX.Auth2.Server.Infrastructure.Auth.Auth2Cookie.Scheme);
        if (session.Succeeded) ctx.User = session.Principal!;
        var tokens = scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx);
        foreach (var header in ctx.Response.Headers.SetCookie) Remember(header!);
        return await SendJsonAsync(method, url, body, null, ("RequestVerificationToken", tokens.RequestToken!));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string? bearer)
    {
        if (_cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        if (bearer is not null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        var response = await _client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var set)) foreach (var h in set) Remember(h);
        return response;
    }

    private void Remember(string setCookie)
    {
        var parts = setCookie.Split(';').Select(p => p.Trim()).ToList();
        var pair = parts[0].Split('=', 2);
        var expired = parts.Any(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase)
            && DateTimeOffset.TryParse(p[8..], out var when) && when < DateTimeOffset.UtcNow);
        if (expired || pair.Length < 2 || pair[1].Length == 0) _cookies.Remove(pair[0]);
        else _cookies[pair[0]] = pair[1];
    }
}

public static class HttpExtensions
{
    public static string? Location(this HttpResponseMessage r) => r.Headers.Location?.OriginalString;

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;

    /// <summary>Reads the payload of a JWT (no signature check; the server already did that).</summary>
    public static JsonElement JwtPayload(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }

    public static IEnumerable<string> Values(this JsonElement e, string claim) =>
        !e.TryGetProperty(claim, out var v) ? [] : v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString()!) : [v.GetString()!];

    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string QueryValue(string url, string key)
    {
        var q = url[(url.IndexOf('?') + 1)..];
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(q)[key].ToString();
    }
}

/// <summary>RFC 6238 TOTP, so tests can act like an authenticator app.</summary>
public static class Totp
{
    public static string Now(string base32Key)
    {
        var key = Decode(base32Key.Replace(" ", "").ToUpperInvariant());
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)((buffer >> bits) & 0xFF)); }
        }
        return [.. bytes];
    }
}
