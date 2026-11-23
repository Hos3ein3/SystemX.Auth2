using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>Answer of the limiter.</summary>
/// <param name="Allowed">False when the limit is used up.</param>
/// <param name="RetryAfter">Time until the window ends.</param>
public readonly record struct RateLimitDecision(bool Allowed, TimeSpan RetryAfter);

/// <summary>Counts requests per key in a fixed window. One implementation in memory, one in Redis (shared by all instances).</summary>
public interface IRateLimiter
{
    /// <summary>Counts one request for <paramref name="key"/> and says whether it is allowed.</summary>
    Task<RateLimitDecision> HitAsync(string key, int limit, TimeSpan window, CancellationToken ct = default);
}

/// <summary>Counters in memory. Correct for one instance only.</summary>
public sealed class MemoryRateLimiter(TimeProvider clock) : IRateLimiter
{
    private sealed class Entry { public long Count; public DateTimeOffset ResetAt; }
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private long _calls;

    /// <inheritdoc />
    public Task<RateLimitDecision> HitAsync(string key, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (Interlocked.Increment(ref _calls) % 2000 == 0) Sweep(now);

        var entry = _entries.GetOrAdd(key, _ => new Entry { ResetAt = now + window });
        lock (entry)
        {
            if (now >= entry.ResetAt) { entry.Count = 0; entry.ResetAt = now + window; }
            entry.Count++;
            return Task.FromResult(new RateLimitDecision(entry.Count <= limit, entry.ResetAt - now));
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
            if (now >= entry.ResetAt) _entries.TryRemove(key, out _);
    }
}

/// <summary>Counters in Redis (INCR + PEXPIRE in one script), so every instance counts together.</summary>
public sealed class RedisRateLimiter(RedisConnection redis) : IRateLimiter
{
    private const string Script = """
        local c = redis.call('INCR', KEYS[1])
        if c == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then redis.call('PEXPIRE', KEYS[1], ARGV[1]); ttl = tonumber(ARGV[1]) end
        return {c, ttl}
        """;

    /// <inheritdoc />
    public async Task<RateLimitDecision> HitAsync(string key, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var result = (RedisResult[])(await redis.Database.ScriptEvaluateAsync(Script, [(RedisKey)redis.Key("rl:" + key)], [(RedisValue)(long)window.TotalMilliseconds]))!;
        var count = (long)result[0];
        var ttl = (long)result[1];
        return new RateLimitDecision(count <= limit, TimeSpan.FromMilliseconds(Math.Max(ttl, 0)));
    }
}

/// <summary>Which policy applies to a request, and who it is counted for.</summary>
internal static class RateLimitPolicyMap
{
    private static readonly string[] IdentityFields = ["client_id", "username", "Input.Login", "Input.UserName", "Input.Email", "Input.Identifier", "Input.Phone"];

    /// <summary>The policy name, or null when the request is not limited.</summary>
    public static async Task<string?> ResolveAsync(HttpRequest r)
    {
        var path = r.Path.Value ?? "";
        var post = HttpMethods.IsPost(r.Method);

        if (path.StartsWith(Auth2Routes.Admin, StringComparison.OrdinalIgnoreCase)) return "admin";
        if (path.StartsWith("/callback/login/", StringComparison.OrdinalIgnoreCase)) return "external";
        if (path.StartsWith("/saml/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/acs", StringComparison.OrdinalIgnoreCase)) return post ? "saml-acs" : null;
        if (!post) return null;
        if (Is(path, "/account/external/challenge") || Is(path, "/connect/external/challenge")) return "external";
        if (path.StartsWith("/account/passkey/", StringComparison.OrdinalIgnoreCase)) return "passkey";

        if (path.Equals(Auth2Routes.Token, StringComparison.OrdinalIgnoreCase))
        {
            if (!r.HasFormContentType) return "token";
            var grant = (await r.ReadFormAsync())["grant_type"].ToString();
            return grant is "password" or Auth2GrantTypes.Otp or Auth2GrantTypes.TwoFactor ? "login" : "token";
        }
        if (Is(path, "/account/login") || Is(path, "/account/twofactor")) return "login";
        if (Is(path, "/account/register") || Is(path, Auth2Routes.Register) || Is(path, Auth2Routes.JoinComplete) || Is(path, "/account/joinapplication")) return "register";
        if (Is(path, "/account/verifyotp") || Is(path, Auth2Routes.OtpRequest) || Is(path, Auth2Routes.EmailConfirm)
            || Is(path, Auth2Routes.PhoneConfirm) || Is(path, "/account/confirmemail") || Is(path, "/account/confirmphone")) return "otp";
        if (Is(path, "/account/forgotpassword") || Is(path, "/account/resetpassword") || Is(path, Auth2Routes.PasswordForgot)
            || Is(path, Auth2Routes.PasswordReset) || Is(path, Auth2Routes.EmailConfirmResend)) return "password-reset";
        if (Is(path, Auth2Routes.Handshake)) return "handshake";
        if (Is(path, Auth2Routes.PermissionsSync) || Is(path, Auth2Routes.RolesSync)) return "permission-sync";
        return null;
    }

    private static bool Is(string path, string route) => path.Equals(route, StringComparison.OrdinalIgnoreCase);

    /// <summary>The login or client id of the request (form field), or a short hash of the bearer token. Never the raw token.</summary>
    public static async Task<string> IdentityAsync(HttpRequest r)
    {
        if (r.HasFormContentType)
        {
            var form = await r.ReadFormAsync();
            // Client and login together: one client may serve many users, and one user may be tried through many clients.
            var parts = IdentityFields.Where(f => form.TryGetValue(f, out var v) && !string.IsNullOrWhiteSpace(v))
                .Select(f => form[f].ToString().Trim().ToLowerInvariant()).ToList();
            if (parts.Count > 0) return string.Join('|', parts);
        }
        var auth = r.Headers.Authorization.ToString();
        return auth.Length == 0 ? "-" : "t:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(auth)))[..12];
    }
}

/// <summary>
/// Rate limiting by named policy. Each request counts twice: for "address + login or client id" (the policy limit),
/// and for the address alone (five times the limit), so spraying many logins from one address is slowed too.
/// Answers <c>429</c> with <c>Retry-After</c>.
/// </summary>
internal sealed class RateLimitMiddleware(RequestDelegate next, IRateLimiter limiter, IOptions<Auth2ServerOptions> options)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var settings = options.Value.RateLimiting;
        if (!settings.Enabled) { await next(http); return; }

        var policy = await RateLimitPolicyMap.ResolveAsync(http.Request);
        if (policy is null) { await next(http); return; }

        var rule = settings.RuleFor(policy);
        var window = TimeSpan.FromSeconds(Math.Max(1, rule.WindowSeconds));
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var identity = await RateLimitPolicyMap.IdentityAsync(http.Request);

        var exact = await limiter.HitAsync($"{policy}:{ip}:{identity}", rule.PermitLimit, window, http.RequestAborted);
        var wide = await limiter.HitAsync($"{policy}:{ip}", rule.PermitLimit * 5, window, http.RequestAborted);
        if (exact.Allowed && wide.Allowed) { await next(http); return; }

        var retry = exact.Allowed ? wide.RetryAfter : exact.RetryAfter;
        Auth2Metrics.RateLimitRejections.Add(1, new KeyValuePair<string, object?>("policy", policy));
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var path = http.Request.Path.Value ?? "";
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/connect/", StringComparison.OrdinalIgnoreCase))
        {
            var text = http.RequestServices.GetRequiredService<IAuth2Localizer>();
            await http.Response.WriteAsJsonAsync(new SystemX.Auth2.Contracts.Common.ApiError(Auth2ErrorCodes.RateLimited, text.Error(Auth2ErrorCodes.RateLimited), null));
        }
    }
}
