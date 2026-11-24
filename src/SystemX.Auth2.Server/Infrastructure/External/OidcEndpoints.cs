using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>The addresses and keys of an upstream provider.</summary>
public sealed record OidcEndpoints(
    string Issuer, Uri AuthorizationEndpoint, Uri TokenEndpoint, Uri? UserInfoEndpoint,
    IReadOnlyList<SecurityKey> SigningKeys, bool TokenAuthBasic, bool IssuerIsTemplate = false);

/// <summary>Finds the endpoints of a provider. Discovery documents and key sets are cached.</summary>
public interface IOidcEndpointResolver
{
    /// <summary>Resolves the endpoints. Throws <see cref="ExternalLoginException"/> with <c>external_claims_invalid</c> when the provider cannot be reached or answers badly.</summary>
    Task<OidcEndpoints> ResolveAsync(RegisteredProvider provider, CancellationToken ct, bool refresh = false);
}

/// <summary>The one HTTP client name used for every call to an upstream provider.</summary>
public static class ExternalHttp
{
    /// <summary>Client name. Its primary handler refuses private network addresses.</summary>
    public const string ClientName = "auth2-external";
}

/// <inheritdoc />
public sealed class OidcEndpointResolver(IHttpClientFactory http, IMemoryCache cache, IOptions<Auth2ServerOptions> options, ILogger<OidcEndpointResolver> log) : IOidcEndpointResolver
{
    private bool Insecure => options.Value.ExternalProviders.AllowPrivateNetworks;

    /// <inheritdoc />
    public async Task<OidcEndpoints> ResolveAsync(RegisteredProvider provider, CancellationToken ct, bool refresh = false)
    {
        var o = (OidcProviderSettings)provider.Settings;
        switch (o.Type)
        {
            case ExternalProviderType.GitHub:
                return new OidcEndpoints("https://github.com", new("https://github.com/login/oauth/authorize"), new("https://github.com/login/oauth/access_token"),
                    new("https://api.github.com/user"), [], false);
            case ExternalProviderType.Facebook:
            {
                var v = string.IsNullOrWhiteSpace(o.ApiVersion) ? "v19.0" : o.ApiVersion;
                return new OidcEndpoints("https://www.facebook.com", new($"https://www.facebook.com/{v}/dialog/oauth"), new($"https://graph.facebook.com/{v}/oauth/access_token"),
                    new($"https://graph.facebook.com/{v}/me"), [], false);
            }
        }
        var authority = o.Type switch
        {
            ExternalProviderType.Google => "https://accounts.google.com",
            ExternalProviderType.Apple => "https://appleid.apple.com",
            ExternalProviderType.Microsoft => $"https://login.microsoftonline.com/{(string.IsNullOrWhiteSpace(o.TenantId) ? "common" : o.TenantId)}/v2.0",
            _ => o.Authority ?? throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "no_authority"),
        };
        if (refresh) cache.Remove("oidc-discovery:" + authority);
        return await cache.GetOrCreateAsync("oidc-discovery:" + authority, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await DiscoverAsync(authority, o.Type, ct);
        }) ?? throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "discovery_failed");
    }

    private async Task<OidcEndpoints> DiscoverAsync(string authority, ExternalProviderType type, CancellationToken ct)
    {
        try
        {
            var client = http.CreateClient(ExternalHttp.ClientName);
            var url = new Uri(authority.TrimEnd('/') + "/.well-known/openid-configuration");
            SsrfGuard.EnsureAllowedUrl(url, Insecure);
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            var root = doc.RootElement;
            var issuer = root.GetProperty("issuer").GetString()!;
            var template = issuer.Contains("{tenantid}", StringComparison.OrdinalIgnoreCase);
            if (!template && !string.Equals(issuer.TrimEnd('/'), authority.TrimEnd('/'), StringComparison.Ordinal) && type != ExternalProviderType.GenericOidc)
                throw new InvalidOperationException("The issuer of the discovery document does not match.");
            if (!template && type == ExternalProviderType.GenericOidc && !string.Equals(issuer.TrimEnd('/'), authority.TrimEnd('/'), StringComparison.Ordinal))
                throw new InvalidOperationException("The issuer of the discovery document does not match the authority.");
            Uri Take(string name) { var u = new Uri(root.GetProperty(name).GetString()!); SsrfGuard.EnsureAllowedUrl(u, Insecure); return u; }
            var jwksUri = Take("jwks_uri");
            var keys = new JsonWebKeySet(await client.GetStringAsync(jwksUri, ct)).GetSigningKeys();
            Uri? userInfo = root.TryGetProperty("userinfo_endpoint", out var ui) && ui.GetString() is { } us ? new Uri(us) : null;
            var basic = false;
            if (root.TryGetProperty("token_endpoint_auth_methods_supported", out var methods))
            {
                var list = methods.EnumerateArray().Select(m => m.GetString()).ToList();
                basic = list.Contains("client_secret_basic") && !list.Contains("client_secret_post");
            }
            return new OidcEndpoints(issuer, Take("authorization_endpoint"), Take("token_endpoint"), userInfo, [.. keys], basic, template);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Discovery of {Authority} failed.", authority);
            throw new ExternalLoginException(Auth2ErrorCodes.ExternalClaimsInvalid, "discovery_failed", ex);
        }
    }
}
