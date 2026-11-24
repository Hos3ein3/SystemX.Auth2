using System.Net.Http.Headers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>
/// OpenID Connect and OAuth 2.0 client for Google, Microsoft, GitHub, Apple, Facebook and generic providers.
/// Authorization code flow with PKCE (S256), <c>state</c> and <c>nonce</c>; the state is single use (shared replay cache) and bound to one provider.
/// </summary>
public sealed class OidcIdentitySource(
    IHttpClientFactory http, IOidcEndpointResolver endpoints, IExternalSecretProtector secrets, IDataProtectionProvider protection,
    IReplayCache replay, IOptions<Auth2ServerOptions> options, TimeProvider clock, ILogger<OidcIdentitySource> log) : IRedirectIdentitySource
{
    private readonly IDataProtector _state = protection.CreateProtector("SystemX.Auth2.ExternalLoginState.v1");

    private sealed record StatePayload(string R, string S, string N, string V, string U, long E, string? L = null);

    /// <inheritdoc />
    public bool Handles(ExternalProviderType type) => type is not (ExternalProviderType.Saml or ExternalProviderType.Ldap);

    private static string Random(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

    /// <inheritdoc />
    public async Task<ExternalChallenge> ChallengeAsync(ExternalChallengeContext ctx, CancellationToken ct)
    {
        var p = ctx.Provider;
        var o = (OidcProviderSettings)p.Settings;
        var ep = await endpoints.ResolveAsync(p, ct);
        var lifetime = TimeSpan.FromMinutes(Math.Max(1, options.Value.ExternalProviders.StateMinutes));
        var state = Random(32);
        var nonce = Random(32);
        var verifier = Random(48);
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var payload = new StatePayload(p.RegistrationName, state, nonce, verifier, ctx.ReturnUrl, clock.GetUtcNow().Add(lifetime).ToUnixTimeSeconds(), ctx.LinkUserId);

        var scopes = o.Scopes.Count > 0 ? o.Scopes : Descriptor(o.Type);
        var query = new List<(string, string)>
        {
            ("response_type", "code"),
            ("client_id", o.ClientId),
            ("redirect_uri", CallbackUrl(ctx.Issuer, p)),
            ("scope", string.Join(" ", scopes)),
            ("state", state),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
        };
        if (IsOidc(o.Type)) query.Add(("nonce", nonce));
        if (o.Type == ExternalProviderType.Apple && scopes.Any(s => s is "name" or "email")) query.Add(("response_mode", "form_post"));
        if (o.Type == ExternalProviderType.Google && !string.IsNullOrWhiteSpace(o.HostedDomain)) query.Add(("hd", o.HostedDomain));
        if (!string.IsNullOrWhiteSpace(ctx.LoginHint) && o.Type != ExternalProviderType.Apple) query.Add(("login_hint", ctx.LoginHint));
        var uri = new UriBuilder(ep.AuthorizationEndpoint) { Query = string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Item1)}={Uri.EscapeDataString(kv.Item2)}")) };
        return new ExternalChallenge(uri.Uri, _state.Protect(JsonSerializer.Serialize(payload)), lifetime);
    }

    private static IReadOnlyList<string> Descriptor(ExternalProviderType type) => type switch
    {
        ExternalProviderType.GitHub => ["read:user", "user:email"],
        ExternalProviderType.Apple => ["name", "email"],
        ExternalProviderType.Facebook => ["public_profile", "email"],
        _ => ["openid", "email", "profile"],
    };

    private static bool IsOidc(ExternalProviderType type) => type is not (ExternalProviderType.GitHub or ExternalProviderType.Facebook);

    private static string CallbackUrl(string issuer, RegisteredProvider p) => issuer.TrimEnd('/') + ExternalProviderUrls.OidcCallbackPath(p.ProjectSlug, p.Name);

    /// <inheritdoc />
    public async Task<ExternalCallbackResult> AuthenticateCallbackAsync(ExternalCallbackContext ctx, CancellationToken ct)
    {
        var p = ctx.Provider;
        var o = (OidcProviderSettings)p.Settings;
        string Fail(string reason) => throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, reason);

        if (string.IsNullOrEmpty(ctx.ProtectedState)) Fail("state_missing");
        StatePayload payload;
        try { payload = JsonSerializer.Deserialize<StatePayload>(_state.Unprotect(ctx.ProtectedState!))!; }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "state_invalid", ex); }

        if (payload.E < clock.GetUtcNow().ToUnixTimeSeconds()) Fail("state_expired");
        if (!string.Equals(payload.R, p.RegistrationName, StringComparison.Ordinal)) Fail("wrong_provider");
        if (!ctx.Parameters.TryGetValue("state", out var returned) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(returned), Encoding.UTF8.GetBytes(payload.S))) Fail("state_mismatch");
        if (ctx.Parameters.ContainsKey("error")) Fail("provider_error");
        // A state can be used one time only, even if the cookie is copied.
        if (!await replay.TryAddAsync(p.ProjectId, $"oidc-state:{p.RegistrationName}:{payload.S}", DateTimeOffset.FromUnixTimeSeconds(payload.E), ct)) Fail("state_replayed");
        if (!ctx.Parameters.TryGetValue("code", out var code) || string.IsNullOrEmpty(code)) Fail("code_missing");

        var ep = await endpoints.ResolveAsync(p, ct);
        var token = await ExchangeAsync(p, o, ep, code!, payload.V, CallbackUrl(ctx.Issuer, p), ct);

        var identity = IsOidc(o.Type)
            ? await FromIdTokenAsync(p, o, ep, token, payload.N, ct)
            : o.Type == ExternalProviderType.GitHub ? await FromGitHubAsync(p, o, ep, token, ct) : await FromFacebookAsync(p, o, ep, token, ct);
        if (o.StoreProviderTokens)
            identity = identity with { Tokens = new ExternalProviderTokens(token.Access, token.Refresh, token.ExpiresIn is { } s ? clock.GetUtcNow().AddSeconds(s) : null) };
        return new ExternalCallbackResult(identity, payload.U, payload.L);
    }

    private sealed record TokenResponse(string? Access, string? Refresh, string? IdToken, int? ExpiresIn);

    private async Task<TokenResponse> ExchangeAsync(RegisteredProvider p, OidcProviderSettings o, OidcEndpoints ep, string code, string verifier, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri, ["code_verifier"] = verifier, ["client_id"] = o.ClientId,
        };
        var secret = o.Type == ExternalProviderType.Apple ? AppleClientSecret(o) : secrets.Unprotect(o.ClientSecretProtected);
        using var request = new HttpRequestMessage(HttpMethod.Post, ep.TokenEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (ep.TokenAuthBasic && secret is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(o.ClientId)}:{Uri.EscapeDataString(secret)}")));
        else if (secret is not null) form["client_secret"] = secret;
        request.Content = new FormUrlEncodedContent(form);
        try
        {
            using var response = await http.CreateClient(ExternalHttp.ClientName).SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "token_request_failed");
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            string? S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (r.TryGetProperty("error", out _)) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "token_request_failed");
            return new TokenResponse(S("access_token"), S("refresh_token"), S("id_token"), r.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var sec) ? sec : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Token request to {Provider} failed: {Kind}.", p.RegistrationName, ex.GetType().Name);
            throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "token_request_failed", ex);
        }
    }

    /// <summary>The client secret of Apple is a short-lived token signed with the private key (ES256).</summary>
    internal string AppleClientSecret(OidcProviderSettings o)
    {
        var pem = secrets.Unprotect(o.PrivateKeyProtected) ?? throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "no_private_key");
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        var key = new ECDsaSecurityKey(ecdsa) { KeyId = o.KeyId };
        var now = clock.GetUtcNow();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.TeamId,
            Audience = "https://appleid.apple.com",
            Subject = new System.Security.Claims.ClaimsIdentity([new("sub", o.ClientId)]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(5).UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<ExternalIdentity> FromIdTokenAsync(RegisteredProvider p, OidcProviderSettings o, OidcEndpoints ep, TokenResponse token, string nonce, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.IdToken)) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "id_token_missing");
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = ep.SigningKeys,
            ValidateAudience = true,
            ValidAudience = o.ClientId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            LifetimeValidator = (nbf, exp, _, _) => (nbf is null || nbf <= clock.GetUtcNow().UtcDateTime.AddMinutes(2)) && exp is not null && exp > clock.GetUtcNow().UtcDateTime.AddMinutes(-2),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuer = false, // checked below, because the rules depend on the provider type
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512, SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384],
        };
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.IdToken, parameters);
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "id_token_invalid", result.Exception);

        if (!string.Equals(Claim(jwt, "nonce"), nonce, StringComparison.Ordinal)) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "nonce_mismatch");
        var iss = jwt.Issuer;
        string? tid = Claim(jwt, "tid");
        switch (o.Type)
        {
            case ExternalProviderType.Microsoft:
                if (string.IsNullOrEmpty(tid) || iss != $"https://login.microsoftonline.com/{tid}/v2.0") throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "issuer_invalid");
                if (!string.IsNullOrWhiteSpace(o.TenantId) && Guid.TryParse(o.TenantId, out _) && !string.Equals(o.TenantId, tid, StringComparison.OrdinalIgnoreCase))
                    throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "tenant_not_allowed");
                if (o.AllowedTenants.Count > 0 && !o.AllowedTenants.Contains(tid, StringComparer.OrdinalIgnoreCase))
                    throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "tenant_not_allowed");
                break;
            case ExternalProviderType.Google:
                if (iss is not ("https://accounts.google.com" or "accounts.google.com")) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "issuer_invalid");
                if (!string.IsNullOrWhiteSpace(o.HostedDomain) && !string.Equals(Claim(jwt, "hd"), o.HostedDomain, StringComparison.OrdinalIgnoreCase))
                    throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "hosted_domain_mismatch");
                break;
            default:
            {
                var allowed = new HashSet<string>(o.AllowedIssuers.Append(ep.Issuer).Select(i => i.TrimEnd('/')), StringComparer.Ordinal);
                if (!allowed.Contains(iss.TrimEnd('/'))) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "issuer_invalid");
                break;
            }
        }
        var sub = Claim(jwt, "sub");
        if (string.IsNullOrEmpty(sub)) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "subject_missing");

        var attrs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in jwt.Claims) attrs[c.Type] = attrs.TryGetValue(c.Type, out var old) ? [.. old, c.Value] : [c.Value];
        // Providers may keep some details in the user info endpoint.
        if (o.Type == ExternalProviderType.GenericOidc && ep.UserInfoEndpoint is not null && token.Access is not null && !attrs.ContainsKey(o.EmailClaim ?? "email"))
            foreach (var kv in await UserInfoAsync(ep.UserInfoEndpoint, token.Access, ct)) attrs.TryAdd(kv.Key, kv.Value);

        var emailClaim = o.Type == ExternalProviderType.GenericOidc ? o.EmailClaim ?? "email" : "email";
        var nameClaim = o.Type == ExternalProviderType.GenericOidc ? o.NameClaim ?? "name" : "name";
        var groups = o.GroupsClaim is { Length: > 0 } g && attrs.TryGetValue(g, out var gv) ? Flatten(gv) : [];
        var amr = attrs.TryGetValue("amr", out var am) ? Flatten(am) : [];
        var verified = attrs.TryGetValue("email_verified", out var ev) ? ev[0].Equals("true", StringComparison.OrdinalIgnoreCase) : (bool?)null;
        var mapped = Map(o, attrs);
        return new ExternalIdentity(p.Name, sub, iss, mapped.GetValueOrDefault("email") ?? First(attrs, emailClaim), verified,
            mapped.GetValueOrDefault("given_name") ?? First(attrs, "given_name"), mapped.GetValueOrDefault("family_name") ?? First(attrs, "family_name"),
            mapped.GetValueOrDefault("phone_number") ?? First(attrs, "phone_number"), groups, attrs)
        {
            DisplayName = mapped.GetValueOrDefault("name") ?? First(attrs, nameClaim),
            Picture = mapped.GetValueOrDefault("picture") ?? First(attrs, "picture"),
            UsedMultiFactor = amr.Count == 0 ? null : amr.Contains("mfa") || (amr.Count >= 2 && amr.Any(a => SecondFactors.Contains(a))),
        };
    }

    private async Task<ExternalIdentity> FromGitHubAsync(RegisteredProvider p, OidcProviderSettings o, OidcEndpoints ep, TokenResponse token, CancellationToken ct)
    {
        if (token.Access is null) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "access_token_missing");
        var user = await GetJsonAsync(ep.UserInfoEndpoint!, token.Access, ct);
        var id = user.TryGetProperty("id", out var idv) ? idv.ToString() : null;
        if (string.IsNullOrEmpty(id)) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "subject_missing");
        string? email = null; var verified = false;
        var emails = await GetJsonAsync(new Uri("https://api.github.com/user/emails"), token.Access, ct);
        foreach (var e in emails.ValueKind == JsonValueKind.Array ? emails.EnumerateArray() : [])
            if (e.TryGetProperty("primary", out var pr) && pr.GetBoolean())
            {
                email = e.GetProperty("email").GetString();
                verified = e.TryGetProperty("verified", out var v) && v.GetBoolean();
            }
        if (o.RequireVerifiedPrimaryEmail && (email is null || !verified)) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "email_not_verified");
        if (o.AllowedOrganizations.Count > 0)
        {
            var orgs = await GetJsonAsync(new Uri("https://api.github.com/user/orgs"), token.Access, ct);
            var logins = (orgs.ValueKind == JsonValueKind.Array ? orgs.EnumerateArray() : []).Select(x => x.GetProperty("login").GetString()).ToList();
            if (!o.AllowedOrganizations.Any(a => logins.Contains(a, StringComparer.OrdinalIgnoreCase))) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "organization_not_allowed");
        }
        var name = Str(user, "name");
        var attrs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["login"] = [Str(user, "login") ?? ""], ["name"] = [name ?? ""], ["avatar_url"] = [Str(user, "avatar_url") ?? ""] };
        var (first, last) = SplitName(name);
        return new ExternalIdentity(p.Name, id, "https://github.com", email, email is null ? null : verified, first, last, null, [], attrs)
        { DisplayName = name ?? Str(user, "login"), Picture = Str(user, "avatar_url") };
    }

    private async Task<ExternalIdentity> FromFacebookAsync(RegisteredProvider p, OidcProviderSettings o, OidcEndpoints ep, TokenResponse token, CancellationToken ct)
    {
        if (token.Access is null) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "access_token_missing");
        var url = new Uri(ep.UserInfoEndpoint + "?fields=id,name,email,first_name,last_name,picture");
        var user = await GetJsonAsync(url, token.Access, ct);
        var id = Str(user, "id");
        if (string.IsNullOrEmpty(id)) throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "subject_missing");
        var attrs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["name"] = [Str(user, "name") ?? ""] };
        // Facebook does not say whether an email is verified, so it never counts as proof unless the admin turns the trust on.
        return new ExternalIdentity(p.Name, id, "https://www.facebook.com", Str(user, "email"), null, Str(user, "first_name"), Str(user, "last_name"), null, [], attrs)
        { DisplayName = Str(user, "name") };
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

    private async Task<Dictionary<string, string[]>> UserInfoAsync(Uri url, string accessToken, CancellationToken ct)
    {
        var json = await GetJsonAsync(url, accessToken, ct);
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in json.EnumerateObject())
            result[prop.Name] = prop.Value.ValueKind == JsonValueKind.Array ? [.. prop.Value.EnumerateArray().Select(x => x.ToString())] : [prop.Value.ToString()];
        return result;
    }

    private static readonly HashSet<string> SecondFactors = ["otp", "sms", "hwk", "swk", "fpt", "face", "retina", "tel", "pop"];

    private static string? Claim(JsonWebToken jwt, string type) => jwt.TryGetClaim(type, out var c) ? c.Value : null;
    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
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
