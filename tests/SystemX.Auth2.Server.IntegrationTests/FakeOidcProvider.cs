using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>A person at the fake provider.</summary>
public sealed class FakeUser
{
    public string Sub { get; set; } = "user-1";
    public string? Email { get; set; } = "sara@example.com";
    public bool? EmailVerified { get; set; } = true;
    public string? Name { get; set; } = "Sara Test";
    public string? GivenName { get; set; } = "Sara";
    public string? FamilyName { get; set; } = "Test";
    public string? Hd { get; set; }
    public string? Tid { get; set; }
    public string[]? Groups { get; set; }
    public string[]? Amr { get; set; }
    public Dictionary<string, object> Extra { get; } = [];
    // GitHub
    public string? GitHubLogin { get; set; } = "sara";
    public bool GitHubEmailVerified { get; set; } = true;
    public string[] GitHubOrgs { get; set; } = [];
}

/// <summary>
/// An in-memory OpenID Connect and OAuth 2.0 provider. It answers for any host name, like Google, Microsoft, GitHub, Apple and Facebook.
/// It checks what a real provider checks: the code is single use and bound to the redirect URI, PKCE is verified, the client secret is recorded.
/// </summary>
public sealed class FakeOidcProvider : HttpMessageHandler
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private sealed record Issued(string ClientId, string RedirectUri, string Challenge, string? Nonce, FakeUser User, string? Issuer);
    private readonly ConcurrentDictionary<string, Issued> _codes = new();
    public ConcurrentQueue<string> Requests { get; } = new();

    // knobs
    public bool TamperSignature { get; set; }
    public string? AudienceOverride { get; set; }
    public string? IssuerOverride { get; set; }
    public bool ExpiredToken { get; set; }
    public string? NonceOverride { get; set; }
    public bool RejectTokenRequest { get; set; }
    public bool MicrosoftStyleIssuer { get; set; }
    public string? ExpectedClientSecret { get; set; }
    public string? LastClientSecret { get; private set; }
    public string? LastClientAuthHeader { get; private set; }
    public int TokenRequests { get; private set; }

    public FakeOidcProvider() => _key = new RsaSecurityKey(_rsa) { KeyId = "k1" };

    /// <summary>The user is "logged in" at the provider and gets a code for the authorization URL the Server built.</summary>
    public string Authorize(Uri authorizationUrl, FakeUser user)
    {
        var q = HttpUtility.ParseQueryString(authorizationUrl.Query);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _codes[code] = new Issued(q["client_id"]!, q["redirect_uri"]!, q["code_challenge"]!, q["nonce"], user, null);
        return code;
    }

    public (string ClientId, string RedirectUri, string State, string Scope, string? Nonce) Parse(Uri url)
    {
        var q = HttpUtility.ParseQueryString(url.Query);
        q["code_challenge_method"].ShouldBe("S256");
        q["response_type"].ShouldBe("code");
        return (q["client_id"]!, q["redirect_uri"]!, q["state"]!, q["scope"]!, q["nonce"]);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        Requests.Enqueue($"{request.Method} {uri.Host}{uri.AbsolutePath}");
        var path = uri.AbsolutePath;
        var origin = $"{uri.Scheme}://{uri.Host}";

        if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
        {
            var prefix = path[..^"/.well-known/openid-configuration".Length];
            var issuer = MicrosoftStyleIssuer ? origin + prefix.Replace(prefix.Split('/')[1], "{tenantid}") : IssuerOverride ?? origin + prefix;
            return Json(new
            {
                issuer,
                authorization_endpoint = origin + prefix + "/authorize",
                token_endpoint = origin + prefix + "/token",
                userinfo_endpoint = origin + prefix + "/userinfo",
                jwks_uri = origin + prefix + "/jwks",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
                scopes_supported = new[] { "openid", "email", "profile" },
            });
        }
        if (path.EndsWith("/jwks", StringComparison.Ordinal))
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = "k1" });
            return Json(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = "k1", n = jwk.N, e = jwk.E } } });
        }
        if (path.EndsWith("/userinfo", StringComparison.Ordinal) && uri.Host != "graph.facebook.com")
        {
            var user = UserOf(request);
            return Json(new { sub = user.Sub, email = user.Email, groups = user.Groups });
        }
        if (path.EndsWith("/token", StringComparison.Ordinal) || path.EndsWith("/access_token", StringComparison.Ordinal))
            return await TokenAsync(request, uri, origin, ct);

        // GitHub API
        if (uri.Host == "api.github.com")
        {
            var user = UserOf(request);
            return path switch
            {
                "/user" => Json(new { id = long.Parse(user.Sub.Replace("gh-", "")), login = user.GitHubLogin, name = user.Name, avatar_url = "https://avatars.test/1.png" }),
                "/user/emails" => Json(new[] { new { email = user.Email, primary = true, verified = user.GitHubEmailVerified } }),
                "/user/orgs" => Json(user.GitHubOrgs.Select(o => new { login = o })),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }
        // Facebook Graph API
        if (uri.Host == "graph.facebook.com" && path.EndsWith("/me", StringComparison.Ordinal))
        {
            var user = UserOf(request);
            return Json(new { id = user.Sub, name = user.Name, email = user.Email, first_name = user.GivenName, last_name = user.FamilyName });
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private readonly ConcurrentDictionary<string, FakeUser> _access = new();
    private FakeUser UserOf(HttpRequestMessage r) =>
        r.Headers.Authorization?.Parameter is { } t && _access.TryGetValue(t, out var u) ? u : throw new HttpRequestException("unauthorized");

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request, Uri uri, string origin, CancellationToken ct)
    {
        TokenRequests++;
        var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(ct));
        LastClientSecret = form["client_secret"];
        LastClientAuthHeader = request.Headers.Authorization?.ToString();
        if (RejectTokenRequest) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
        if (ExpectedClientSecret is not null && LastClientSecret != ExpectedClientSecret) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        if (!_codes.TryRemove(form["code"] ?? "", out var issued)) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
        if (form["redirect_uri"] != issued.RedirectUri || form["client_id"] != issued.ClientId) return new HttpResponseMessage(HttpStatusCode.BadRequest);
        var expected = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"] ?? "")));
        if (expected != issued.Challenge) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };

        var u = issued.User;
        var access = "at-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        _access[access] = u;
        var iss = IssuerOverride ?? (MicrosoftStyleIssuer ? $"https://login.microsoftonline.com/{u.Tid}/v2.0" : origin == "https://accounts.google.com" ? origin : origin + uri.AbsolutePath[..uri.AbsolutePath.LastIndexOf('/')]);
        var claims = new Dictionary<string, object> { ["sub"] = u.Sub };
        if (u.Email is not null) claims["email"] = u.Email;
        if (u.EmailVerified is not null) claims["email_verified"] = u.EmailVerified.Value;
        if (u.Name is not null) claims["name"] = u.Name;
        if (u.GivenName is not null) claims["given_name"] = u.GivenName;
        if (u.FamilyName is not null) claims["family_name"] = u.FamilyName;
        if (u.Hd is not null) claims["hd"] = u.Hd;
        if (u.Tid is not null) claims["tid"] = u.Tid;
        if (u.Groups is not null) claims["groups"] = u.Groups;
        if (u.Amr is not null) claims["amr"] = u.Amr;
        claims["nonce"] = NonceOverride ?? issued.Nonce ?? "";
        foreach (var (k, v) in u.Extra) claims[k] = v;
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = iss,
            Audience = AudienceOverride ?? issued.ClientId,
            Claims = claims,
            IssuedAt = ExpiredToken ? now.AddHours(-2) : now.AddMinutes(-1),   // real providers always send iat
            NotBefore = ExpiredToken ? now.AddHours(-2) : now.AddMinutes(-1),
            Expires = ExpiredToken ? now.AddHours(-1) : now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        };
        var idToken = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
        if (TamperSignature) idToken = idToken[..^6] + (idToken[^6] == 'A' ? "BBBBBB" : "AAAAAA");
        return Json(new { access_token = access, token_type = "bearer", id_token = idToken, refresh_token = "rt-1", expires_in = 3600 });
    }

    private static HttpResponseMessage Json(object o) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };
}
