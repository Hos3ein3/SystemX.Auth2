using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>Adds security headers to every response (unless the endpoint already set them).</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<Auth2ServerOptions> options)
{
    public Task InvokeAsync(HttpContext http)
    {
        http.Response.OnStarting(() =>
        {
            var h = http.Response.Headers;
            h.TryAdd("X-Content-Type-Options", "nosniff");
            h.TryAdd("Referrer-Policy", "no-referrer");
            h.TryAdd("X-Frame-Options", "DENY");
            h.TryAdd("Content-Security-Policy", options.Value.Security.ContentSecurityPolicy);
            h.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
            // Token and account answers must not be stored by caches.
            var path = http.Request.Path.Value ?? "";
            if (path.StartsWith("/connect/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                h.TryAdd("Cache-Control", "no-store");
                h.TryAdd("Pragma", "no-cache");
            }
            return Task.CompletedTask;
        });
        return next(http);
    }
}

/// <summary>
/// CORS is off by default. An origin is allowed when it is listed in <c>Clients.AllowedCorsOrigins</c> of an enabled application
/// (exact match, no wildcards). Cookies and credentials are never allowed across origins.
/// </summary>
internal sealed class Auth2CorsPolicyProvider(IServiceScopeFactory scopes, IMemoryCache cache) : ICorsPolicyProvider
{
    private const string CacheKey = "auth2:cors-origins";

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return null;
        var allowed = await AllowedOriginsAsync(context.RequestAborted);
        if (!allowed.Contains(origin)) return new CorsPolicyBuilder().Build();   // an empty policy allows nothing
        return new CorsPolicyBuilder().WithOrigins(origin).WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "Accept-Language", "If-Match", "X-Auth2-Application").Build();
    }

    private async Task<HashSet<string>> AllowedOriginsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out HashSet<string>? hit) && hit is not null) return hit;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var settings = await db.Applications.AsNoTracking().Where(a => a.IsEnabled).Select(a => a.Settings).ToListAsync(ct);
        var set = new HashSet<string>(settings.SelectMany(s => s.Clients.AllowedCorsOrigins).Select(o => o.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);
        cache.Set(CacheKey, set, TimeSpan.FromSeconds(60));   // short on purpose: a changed list applies within a minute on every instance
        return set;
    }
}

/// <summary>Ready only when the database answers.</summary>
internal sealed class DatabaseHealthCheck(Auth2DbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try { return await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The database does not answer."); }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("The database does not answer.", ex); }
    }
}

/// <summary>Ready only when Redis answers (checked only when Redis is configured).</summary>
internal sealed class RedisHealthCheck(RedisConnection redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try { await redis.Database.PingAsync(); return HealthCheckResult.Healthy(); }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("Redis does not answer.", ex); }
    }
}

/// <summary>Forwarded headers (reverse proxy) and the order of the hardening middleware.</summary>
internal static class HardeningSetup
{
    public static void ConfigureForwardedHeaders(IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<Auth2ServerOptions>>((o, server) =>
        {
            var s = server.Value.Security;
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            if (s.TrustAllProxies)
            {
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            }
            foreach (var p in s.KnownProxies)
                if (System.Net.IPAddress.TryParse(p, out var ip)) o.KnownProxies.Add(ip);
        });
    }

    public static bool RequireHttps(IHostEnvironment env, Auth2ServerOptions options) =>
        options.Security.RequireHttps ?? !OpenIddict.OpenIddictSetup.IsDevelopmentLike(env);
}
