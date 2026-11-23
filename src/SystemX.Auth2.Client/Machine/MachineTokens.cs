using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Client.Machine;

/// <summary>Gets client-credentials tokens ("machine tokens") for this application. Tokens are cached until shortly before they expire.</summary>
public interface IAuth2MachineTokenProvider
{
    /// <summary>
    /// A valid access token for the scopes. Cached until <c>expires_in - 60 seconds</c> (at least 30 seconds), one request at a time per scope set.
    /// </summary>
    /// <exception cref="Auth2Exception">The server is unreachable (<c>IsTransient</c>), or it refused the client id or secret.</exception>
    Task<string> GetTokenAsync(IEnumerable<string>? scopes = null, CancellationToken ct = default);

    /// <summary>Forgets cached tokens. The next call asks the server again (used after a 401).</summary>
    void Invalidate();
}

internal sealed class MachineTokenProvider(IHttpClientFactory http, IOptions<Auth2ClientOptions> options, IOptions<Auth2MachineOptions> machine, TimeProvider clock)
    : IAuth2MachineTokenProvider
{
    private sealed record Entry(string Token, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<string> GetTokenAsync(IEnumerable<string>? scopes = null, CancellationToken ct = default)
    {
        var list = (scopes ?? machine.Value.Scopes).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var key = string.Join(' ', list);
        if (TryGet(key, out var token)) return token;

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (TryGet(key, out token)) return token;   // another caller got it while we waited: one request for everybody
            var entry = await RequestAsync(list, ct);
            _cache[key] = entry;
            return entry.Token;
        }
        finally { gate.Release(); }
    }

    public void Invalidate() => _cache.Clear();

    private bool TryGet(string key, out string token)
    {
        if (_cache.TryGetValue(key, out var e) && clock.GetUtcNow() < e.ExpiresAt) { token = e.Token; return true; }
        token = "";
        return false;
    }

    private async Task<Entry> RequestAsync(string[] scopes, CancellationToken ct)
    {
        var o = options.Value;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = o.ClientId, ["client_secret"] = o.ClientSecret,
        };
        if (scopes.Length > 0) form["scope"] = string.Join(' ', scopes);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/token") { Content = new FormUrlEncodedContent(form) };
            response = await http.CreateClient(Auth2ClientDefaults.ResilientHttpClientName).SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            throw new Auth2Exception(null, Auth2ClientErrorCodes.ServerUnavailable, "The server could not be reached to get a machine token.", isTransient: true, inner: ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    var token = doc.RootElement.GetProperty("access_token").GetString()!;
                    var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
                    var lifetime = TimeSpan.FromSeconds(Math.Max(30, expiresIn - 60));
                    return new Entry(token, clock.GetUtcNow() + lifetime);
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    throw new Auth2Exception(status, Auth2ClientErrorCodes.InvalidResponse, "The token answer of the server could not be understood.", isTransient: false, inner: ex);
                }
            }
            if (status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new Auth2Exception(status, status >= 500 ? Auth2ClientErrorCodes.ServerUnavailable : Auth2ErrorCodes.RateLimited,
                    $"The server answered {status} when a machine token was requested.", isTransient: true);

            // 400 or 401: wrong client id or secret, or a scope this client may not use. The body has only an error code, never a secret.
            var code = await ReadErrorCodeAsync(response, ct);
            throw new Auth2Exception(status, code, $"The server refused the machine token request ({code}). Check ClientId, ClientSecret and the scopes.");
        }
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error_code", out var c) && c.GetString() is { Length: > 0 } code) return code;
            if (doc.RootElement.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } error) return error;
        }
        catch (JsonException) { /* not JSON: fall through */ }
        return "invalid_client";
    }
}

/// <summary>Adds a machine token to every request of an HTTP client. On a 401 the cached token is dropped, so the next call gets a new one.</summary>
public sealed class MachineTokenHandler(IAuth2MachineTokenProvider tokens, IEnumerable<string>? scopes = null) : DelegatingHandler
{
    private readonly string[]? _scopes = scopes?.ToArray();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Authorization is null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(_scopes, ct));
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Invalidate();
        return response;
    }
}

/// <summary>Helpers for programs that call other APIs with a machine token (background services).</summary>
public interface IAuth2MachineService
{
    /// <summary>
    /// An <see cref="HttpClient"/> from <c>IHttpClientFactory</c> by name. Register it with
    /// <c>services.AddHttpClient(name).AddAuth2MachineToken("scope")</c>, then every request carries a machine token.
    /// </summary>
    HttpClient CreateClient(string name);

    /// <summary>A token for the scopes (cached).</summary>
    Task<string> GetTokenAsync(IEnumerable<string>? scopes = null, CancellationToken ct = default);
}

internal sealed class MachineService(IHttpClientFactory http, IAuth2MachineTokenProvider tokens) : IAuth2MachineService
{
    public HttpClient CreateClient(string name) => http.CreateClient(name);
    public Task<string> GetTokenAsync(IEnumerable<string>? scopes = null, CancellationToken ct = default) => tokens.GetTokenAsync(scopes, ct);
}
