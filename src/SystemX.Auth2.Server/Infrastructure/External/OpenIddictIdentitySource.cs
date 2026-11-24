using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenIddict.Client.AspNetCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>
/// Social login (Google, Microsoft, GitHub, Apple, Facebook) and generic OpenID Connect, done by <b>OpenIddict.Client</b> (+ <c>OpenIddict.Client.WebIntegration</c>).
/// OpenIddict does the protocol: discovery, PKCE, <c>state</c>, <c>nonce</c>, the token request and the checks of the identity token.
/// This class adds what is Auth2's own: one registration per provider and project (<see cref="ExternalClientRegistrations"/>), the single-use state, the
/// provider rules (tenant, hosted domain, verified GitHub email, organizations) and the mapping to <see cref="ExternalIdentity"/>.
/// </summary>
/// <remarks>
/// OpenIddict's ASP.NET Core host needs an <c>HttpContext</c>. The page that starts or ends a sign-in is Auth2's own endpoint, so the source runs the OpenIddict
/// handler on a small private context (same address, no real response) and takes the redirect and the cookies of OpenIddict from it. The cookies travel inside the
/// sealed state that <see cref="ExternalFlow"/> already keeps in its own short-lived cookie. This keeps one flow for every provider type.
/// </remarks>
public sealed class OpenIddictIdentitySource(
    IServiceScopeFactory scopes, IHttpClientFactory http, IDataProtectionProvider protection,
    IReplayCache replay, IOptions<Auth2ServerOptions> options, TimeProvider clock, ILogger<OpenIddictIdentitySource> log) : IRedirectIdentitySource
{
    private readonly IDataProtector _state = protection.CreateProtector("SystemX.Auth2.ExternalLoginState.v2");

    private sealed record StatePayload(string R, string[] C, string U, long E, string? L = null);

    /// <inheritdoc />
    public bool Handles(ExternalProviderType type) => type is not (ExternalProviderType.Saml or ExternalProviderType.Ldap);

    private static DefaultHttpContext NewContext(IServiceProvider services, string issuer, string path, string method)
    {
        var uri = new Uri(issuer);
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Request.Scheme = uri.Scheme;
        ctx.Request.Host = uri.IsDefaultPort ? new HostString(uri.Host) : new HostString(uri.Host, uri.Port);
        ctx.Request.PathBase = uri.AbsolutePath.TrimEnd('/');
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    /// <summary>
    /// Runs the request step of the OpenIddict handler, as <c>UseAuthentication</c> does for a real request: it creates the per-request OpenIddict context.
    /// The path of the private context is no redirection endpoint of OpenIddict, so the handler only prepares and returns.
    /// </summary>
    private static async Task PrepareAsync(HttpContext ctx)
    {
        var handler = await ctx.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>().GetHandlerAsync(ctx, OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
        if (handler is IAuthenticationRequestHandler request) await request.HandleRequestAsync();
    }

    /// <summary>
    /// OpenIddict finds "the current request" through <c>IHttpContextAccessor</c> in places. During real traffic that is the real request, so the private context
    /// must take its place while the OpenIddict handler works, and the real one must come back afterwards. Returns what restores it.
    /// </summary>
    private static IDisposable Scoped(HttpContext ctx)
    {
        var accessor = ctx.RequestServices.GetService<IHttpContextAccessor>();
        if (accessor is null) return NoRestore.Instance;
        var saved = accessor.HttpContext;
        accessor.HttpContext = ctx;
        return new Restore(accessor, saved);
    }

    private sealed class NoRestore : IDisposable { public static readonly NoRestore Instance = new(); public void Dispose() { } }
    private sealed class Restore(IHttpContextAccessor accessor, HttpContext? saved) : IDisposable { public void Dispose() => accessor.HttpContext = saved; }

    /// <inheritdoc />
    public async Task<ExternalChallenge> ChallengeAsync(ExternalChallengeContext c, CancellationToken ct)
    {
        var p = c.Provider;
        var lifetime = TimeSpan.FromMinutes(Math.Max(1, options.Value.ExternalProviders.StateMinutes));
        await using var scope = scopes.CreateAsyncScope();
        // A challenge may not start at a redirection endpoint of OpenIddict, so the private context has a neutral path. The callback context has the real one.
        var ctx = NewContext(scope.ServiceProvider, c.Issuer, "/auth2/external/start", HttpMethods.Get);
        ctx.RequestAborted = ct;

        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictClientAspNetCoreConstants.Properties.ProviderName] = p.RegistrationName,
        }) { RedirectUri = "/" };
        if (!string.IsNullOrWhiteSpace(c.LoginHint)) properties.SetString(OpenIddictClientAspNetCoreConstants.Properties.LoginHint, c.LoginHint);
        var o = (OidcProviderSettings)p.Settings;
        if (o.Type == ExternalProviderType.Google && !string.IsNullOrWhiteSpace(o.HostedDomain)) properties.Parameters["hd"] = o.HostedDomain;   // sent as an extra authorization parameter

        try { using var _ = Scoped(ctx); await PrepareAsync(ctx); await ctx.ChallengeAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme, properties); }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("The sign-in with {Provider} could not start: {Kind}.", p.RegistrationName, ex.GetType().Name);
            throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "discovery_failed", ex);
        }
        var location = ctx.Response.Headers.Location.ToString();
        if (string.IsNullOrEmpty(location) || !Uri.TryCreate(location, UriKind.Absolute, out var redirect))
        {
            log.LogWarning("The challenge of {Provider} gave status {Status} and no address.", p.RegistrationName, ctx.Response.StatusCode);
            // OpenIddict answers a challenge it cannot start with an error page. ID2170 is "the provider cannot be reached or its configuration is invalid".
            ctx.Response.Body.Position = 0;
            var body = new StreamReader(ctx.Response.Body).ReadToEnd();
            var discovery = body.Contains("ID2170", StringComparison.Ordinal);
            log.LogWarning("The challenge of {Provider} gave status {Status} ({Kind}).", p.RegistrationName, ctx.Response.StatusCode, discovery ? "discovery" : "other");
            throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, discovery ? "discovery_failed" : "challenge_failed");
        }

        var payload = new StatePayload(p.RegistrationName, Cookies(ctx.Response), c.ReturnUrl, clock.GetUtcNow().Add(lifetime).ToUnixTimeSeconds(), c.LinkUserId);
        return new ExternalChallenge(redirect, _state.Protect(JsonSerializer.Serialize(payload)), lifetime);
    }

    /// <summary>The <c>name=value</c> pairs of the cookies OpenIddict set (deleted cookies are left out).</summary>
    private static string[] Cookies(HttpResponse response) =>
        [.. response.Headers.SetCookie.Select(v => v ?? "").Where(v => !v.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)).Select(v => v.Split(';')[0]).Where(v => v.Contains('='))];

    /// <inheritdoc />
    public async Task<ExternalCallbackResult> AuthenticateCallbackAsync(ExternalCallbackContext c, CancellationToken ct)
    {
        var p = c.Provider;
        var o = (OidcProviderSettings)p.Settings;
        static ExternalLoginException Fail(string reason, Exception? inner = null) => new(Auth2ErrorCodes.ExternalClaimsInvalid, reason, inner);

        if (string.IsNullOrEmpty(c.ProtectedState)) throw Fail("state_missing");
        StatePayload payload;
        try { payload = JsonSerializer.Deserialize<StatePayload>(_state.Unprotect(c.ProtectedState))!; }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException) { throw Fail("state_invalid", ex); }
        if (payload.E < clock.GetUtcNow().ToUnixTimeSeconds()) throw Fail("state_expired");
        if (!string.Equals(payload.R, p.RegistrationName, StringComparison.Ordinal)) throw Fail("wrong_provider");
        if (c.Parameters.ContainsKey("error")) throw Fail("provider_error");
        if (!c.Parameters.TryGetValue("state", out var returned) || string.IsNullOrEmpty(returned)) throw Fail("state_missing");
        // A state can be used one time only, also when somebody keeps a copy of the cookie.
        if (!await replay.TryAddAsync(p.ProjectId, $"oidc-state:{p.RegistrationName}:{returned}", DateTimeOffset.FromUnixTimeSeconds(payload.E), ct)) throw Fail("state_replayed");
        if (!c.Parameters.ContainsKey("code")) throw Fail("code_missing");

        await using var scope = scopes.CreateAsyncScope();
        // Apple answers with a form post, all others with the query.
        var form = o.Type == ExternalProviderType.Apple;
        var ctx = NewContext(scope.ServiceProvider, c.Issuer, ExternalProviderUrls.OidcCallbackPath(p.ProjectSlug, p.Name), form ? HttpMethods.Post : HttpMethods.Get);
        ctx.RequestAborted = ct;
        if (form)
        {
            ctx.Request.ContentType = "application/x-www-form-urlencoded";
            ctx.Request.Form = new FormCollection(c.Parameters.ToDictionary(kv => kv.Key, kv => new StringValues(kv.Value)));
        }
        else ctx.Request.QueryString = QueryString.Create(c.Parameters.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
        ctx.Request.Headers.Cookie = string.Join("; ", payload.C);

        AuthenticateResult result;
        try { using var _ = Scoped(ctx); await PrepareAsync(ctx); result = await ctx.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme); }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("The answer of {Provider} could not be checked: {Kind}.", p.RegistrationName, ex.GetType().Name);
            throw Fail("token_request_failed", ex);
        }
        if (!result.Succeeded || result.Principal is null)
        {
            var fail = Fail(ReasonOf(result), result.Failure);
            var errorId = ErrorId(result);   // an id like ID2128 and the OAuth error word: never a value of the request
            fail.Data["openiddict"] = errorId + " " + (result.Properties?.Items.TryGetValue(".error", out var errorWord) == true ? errorWord : "") + " " + (result.Properties?.Items.TryGetValue(".error_description", out var errorText) == true ? errorText : "");   // fixed library texts
            log.LogWarning("The answer of {Provider} was refused: {Reason} ({OpenIddictError}).", p.RegistrationName, fail.Reason, errorId);
            throw fail;
        }

        var principal = result.Principal;
        // OpenIddict accepts an identity token that arrives over the back channel without looking at its lifetime. We do (two minutes of clock difference).
        if (long.TryParse(Value(principal, Claims.ExpiresAt), out var exp) && DateTimeOffset.FromUnixTimeSeconds(exp) < clock.GetUtcNow().AddMinutes(-2)) throw Fail("id_token_invalid");
        var identity = o.Type switch
        {
            ExternalProviderType.GitHub => await FromGitHubAsync(p, o, principal, result.Properties, ct),
            ExternalProviderType.Facebook => FromFacebook(p, principal),
            _ => FromClaims(p, o, principal),
        };
        if (o.StoreProviderTokens)
        {
            var access = result.Properties?.GetTokenValue("backchannel_access_token");
            var refresh = result.Properties?.GetTokenValue("refresh_token");
            DateTimeOffset? expires = DateTimeOffset.TryParse(result.Properties?.GetTokenValue("backchannel_access_token_expiration"), out var e) ? e : null;
            identity = identity with { Tokens = new ExternalProviderTokens(access, refresh, expires) };
        }
        return new ExternalCallbackResult(identity, payload.U, payload.L);
    }

    /// <summary>
    /// A short safe word for the audit log, from the OpenIddict error id (for example <c>ID2128</c>, a wrong nonce). The text of a failure can name values of the
    /// request, so nothing of it is copied.
    /// </summary>
    private static string? ErrorId(AuthenticateResult result) =>
        result.Properties?.Items.TryGetValue(".error_uri", out var uri) == true ? uri?.Split('/')[^1] : null;

    private static string ReasonOf(AuthenticateResult result)
    {
        string? Item(string key) => result.Properties?.Items.TryGetValue(key, out var v) == true ? v : null;
        var id = ErrorId(result);
        return id switch
        {
            "ID2004" or "ID2005" or "ID2006" or "ID2007" or "ID2008" => "state_mismatch",   // the state token is missing, changed, expired or not for this request
            "ID2128" => "nonce_mismatch",
            "ID2088" => "issuer_invalid",
            "ID2091" or "ID2127" or "ID2090" or "ID2089" => "id_token_invalid",             // signature, audience, lifetime
            _ => Item(".error") switch
            {
                "invalid_token" => "id_token_invalid",
                "invalid_request" or "invalid_grant" or "invalid_client" => "token_request_failed",
                "access_denied" => "provider_error",
                _ => "callback_invalid",
            },
        };
    }

    // ---- identities --------------------------------------------------------------------------------------------------------------------------------------

    private static string? Value(ClaimsPrincipal p, string type) => p.FindFirst(type)?.Value is { Length: > 0 } v ? v : null;

    private static Dictionary<string, string[]> Attributes(ClaimsPrincipal principal)
    {
        var attrs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in principal.Claims) attrs[c.Type] = attrs.TryGetValue(c.Type, out var old) ? [.. old, c.Value] : [c.Value];
        return attrs;
    }

    private ExternalIdentity FromClaims(RegisteredProvider p, OidcProviderSettings o, ClaimsPrincipal principal)
    {
        string Fail(string reason) => throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, reason);
        var attrs = Attributes(principal);
        var iss = Value(principal, Claims.Issuer) ?? Value(principal, "iss") ?? "";
        var sub = Value(principal, Claims.Subject);
        if (string.IsNullOrEmpty(sub)) Fail("subject_missing");

        switch (o.Type)
        {
            case ExternalProviderType.Microsoft:
                var tid = Value(principal, "tid");
                if (string.IsNullOrEmpty(tid) || iss != $"https://login.microsoftonline.com/{tid}/v2.0") Fail("issuer_invalid");
                if (!string.IsNullOrWhiteSpace(o.TenantId) && Guid.TryParse(o.TenantId, out _) && !string.Equals(o.TenantId, tid, StringComparison.OrdinalIgnoreCase)) Fail("tenant_not_allowed");
                if (o.AllowedTenants.Count > 0 && !o.AllowedTenants.Contains(tid!, StringComparer.OrdinalIgnoreCase)) Fail("tenant_not_allowed");
                break;
            case ExternalProviderType.Google:
                if (iss is not ("https://accounts.google.com" or "accounts.google.com")) Fail("issuer_invalid");
                if (!string.IsNullOrWhiteSpace(o.HostedDomain) && !string.Equals(Value(principal, "hd"), o.HostedDomain, StringComparison.OrdinalIgnoreCase)) Fail("hosted_domain_mismatch");
                break;
            case ExternalProviderType.Apple:
                if (iss != "https://appleid.apple.com") Fail("issuer_invalid");
                break;
        }

        var emailClaim = o.Type == ExternalProviderType.GenericOidc ? o.EmailClaim ?? "email" : "email";
        var nameClaim = o.Type == ExternalProviderType.GenericOidc ? o.NameClaim ?? "name" : "name";
        var groups = o.GroupsClaim is { Length: > 0 } g && attrs.TryGetValue(g, out var gv) ? Flatten(gv) : [];
        var amr = attrs.TryGetValue("amr", out var am) ? Flatten(am) : [];
        var verified = attrs.TryGetValue("email_verified", out var ev) ? ev[0].Equals("true", StringComparison.OrdinalIgnoreCase) : (bool?)null;
        var mapped = Map(o, attrs);
        return new ExternalIdentity(p.Name, sub!, iss, mapped.GetValueOrDefault("email") ?? First(attrs, emailClaim), verified,
            mapped.GetValueOrDefault("given_name") ?? First(attrs, "given_name"), mapped.GetValueOrDefault("family_name") ?? First(attrs, "family_name"),
            mapped.GetValueOrDefault("phone_number") ?? First(attrs, "phone_number"), groups, attrs)
        {
            DisplayName = mapped.GetValueOrDefault("name") ?? First(attrs, nameClaim),
            Picture = mapped.GetValueOrDefault("picture") ?? First(attrs, "picture"),
            UsedMultiFactor = amr.Count == 0 ? null : amr.Contains("mfa") || (amr.Count >= 2 && amr.Any(a => SecondFactors.Contains(a))),
        };
    }

    private async Task<ExternalIdentity> FromGitHubAsync(RegisteredProvider p, OidcProviderSettings o, ClaimsPrincipal principal, AuthenticationProperties? props, CancellationToken ct)
    {
        string Fail(string reason) => throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, reason);
        var access = props?.GetTokenValue("backchannel_access_token") ?? Fail("access_token_missing");
        var id = Value(principal, "id") ?? Value(principal, ClaimTypes.NameIdentifier) ?? Value(principal, Claims.Subject);   // the library keeps the raw GitHub id and also maps it
        if (string.IsNullOrEmpty(id))
        {
            var missing = new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "subject_missing");
            missing.Data["claimTypes"] = string.Join(",", principal.Claims.Select(c => c.Type));   // names only, never values
            throw missing;
        }

        // The user endpoint may show an email that GitHub has not verified. Only the primary email of /user/emails says whether it is verified.
        string? email = null; var verified = false;
        var emails = await GetJsonAsync(new Uri("https://api.github.com/user/emails"), access, ct);
        foreach (var e in emails.ValueKind == JsonValueKind.Array ? emails.EnumerateArray() : [])
            if (e.TryGetProperty("primary", out var pr) && pr.GetBoolean())
            {
                email = e.GetProperty("email").GetString();
                verified = e.TryGetProperty("verified", out var v) && v.GetBoolean();
            }
        if (o.RequireVerifiedPrimaryEmail && (email is null || !verified)) Fail("email_not_verified");
        if (o.AllowedOrganizations.Count > 0)
        {
            var orgs = await GetJsonAsync(new Uri("https://api.github.com/user/orgs"), access, ct);
            var logins = (orgs.ValueKind == JsonValueKind.Array ? orgs.EnumerateArray() : []).Select(x => x.GetProperty("login").GetString()).ToList();
            if (!o.AllowedOrganizations.Any(a => logins.Contains(a, StringComparer.OrdinalIgnoreCase))) Fail("organization_not_allowed");
        }
        var name = Value(principal, Claims.Name) ?? Value(principal, "name");
        var login = Value(principal, Claims.PreferredUsername) ?? Value(principal, "login");
        var attrs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["login"] = [login ?? ""], ["name"] = [name ?? ""], ["avatar_url"] = [Value(principal, "picture") ?? Value(principal, "avatar_url") ?? ""] };
        var (first, last) = SplitName(name);
        return new ExternalIdentity(p.Name, id!, "https://github.com", email, email is null ? null : verified, first, last, null, [], attrs)
        { DisplayName = name ?? login, Picture = attrs["avatar_url"][0] is { Length: > 0 } pic ? pic : null };
    }

    private static ExternalIdentity FromFacebook(RegisteredProvider p, ClaimsPrincipal principal)
    {
        var id = Value(principal, Claims.Subject) ?? Value(principal, "id") ?? throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "subject_missing");
        var attrs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["name"] = [Value(principal, "name") ?? ""] };
        // Facebook does not say whether an email is verified, so it never counts as proof unless the admin turns the trust on.
        return new ExternalIdentity(p.Name, id, "https://www.facebook.com", Value(principal, "email"), null, Value(principal, Claims.GivenName) ?? Value(principal, "first_name"),
            Value(principal, Claims.FamilyName) ?? Value(principal, "last_name"), null, [], attrs)
        { DisplayName = Value(principal, "name") };
    }

    private async Task<JsonElement> GetJsonAsync(Uri url, string accessToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("SystemX-Auth2");
            using var response = await http.CreateClient(ExternalHttp.ClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "userinfo_failed");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "userinfo_failed", ex);
        }
    }

    private static readonly HashSet<string> SecondFactors = ["otp", "sms", "hwk", "swk", "fpt", "face", "retina", "tel", "pop"];
    private static string? First(Dictionary<string, string[]> a, string key) => a.TryGetValue(key, out var v) && v.Length > 0 && v[0].Length > 0 ? v[0] : null;
    private static List<string> Flatten(string[] values) => [.. values.SelectMany(v => v.StartsWith('[') ? TryArray(v) : [v])];
    private static IEnumerable<string> TryArray(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; } catch (JsonException) { return [json]; }
    }

    private static (string? First, string? Last) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (null, null);
        var parts = name.Trim().Split(' ', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    /// <summary>Applies <c>ClaimMappings</c> (source name to local field) to the attributes.</summary>
    private static Dictionary<string, string> Map(ExternalProviderSettings s, Dictionary<string, string[]> attrs)
    {
        var result = new Dictionary<string, string>();
        foreach (var (source, local) in s.ClaimMappings)
            if (attrs.TryGetValue(source, out var v) && v.Length > 0 && !string.IsNullOrEmpty(v[0])) result[local] = v[0];
        return result;
    }
}
