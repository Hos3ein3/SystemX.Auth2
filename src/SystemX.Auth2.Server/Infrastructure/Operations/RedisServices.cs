using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>
/// One-time codes in Redis, same rules as the table version: hashed, expire, wrong-attempt limit, resend delay, work once
/// (deleting the key is the "use": only one caller gets "1 deleted"). Redis expires the key by itself.
/// </summary>
public sealed class RedisOtpService(RedisConnection redis, IAuthFlowResolver flow, ISecretHasher hasher, TimeProvider clock) : IOtpService
{
    private const string IssueScript = """
        local c = redis.call('HGET', KEYS[1], 'c')
        if c and tonumber(ARGV[1]) < tonumber(c) + tonumber(ARGV[2]) then return 0 end
        redis.call('DEL', KEYS[1])
        redis.call('HSET', KEYS[1], 'h', ARGV[3], 'a', 0, 'c', ARGV[1], 'e', ARGV[4])
        redis.call('PEXPIRE', KEYS[1], ARGV[5])
        return 1
        """;

    private async Task<RedisKey> KeyAsync(AppUser user, string purpose, CancellationToken ct)
    {
        var app = await flow.GetApplicationAsync(ct);
        return redis.Key($"otp:{app.Id:N}:{user.Id:N}:{purpose}");
    }

    /// <inheritdoc />
    public async Task<OtpIssueResult> IssueAsync(AppUser user, string purpose, CancellationToken ct = default)
    {
        var rules = (await flow.GetSettingsAsync(ct)).TwoFactor;
        var key = await KeyAsync(user, purpose, ct);
        var now = clock.GetUtcNow();
        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, rules.OtpLength)).ToString("D" + rules.OtpLength, System.Globalization.CultureInfo.InvariantCulture);
        var expires = now.AddSeconds(rules.OtpLifetimeSeconds).ToUnixTimeMilliseconds();
        var ok = (long)(await redis.Database.ScriptEvaluateAsync(IssueScript, [key],
            [now.ToUnixTimeMilliseconds(), rules.OtpResendSeconds * 1000L, hasher.Hash(code), expires, rules.OtpLifetimeSeconds * 1000L]))!;
        return ok == 1 ? new OtpIssueResult(code, TooSoon: false) : new OtpIssueResult(null, TooSoon: true);
    }

    /// <inheritdoc />
    public async Task<OtpVerifyResult> VerifyAsync(AppUser user, string purpose, string code, CancellationToken ct = default)
    {
        var rules = (await flow.GetSettingsAsync(ct)).TwoFactor;
        var key = await KeyAsync(user, purpose, ct);
        var db = redis.Database;
        var fields = await db.HashGetAllAsync(key);
        if (fields.Length == 0) return OtpVerifyResult.NotFound;
        var map = fields.ToDictionary(f => (string)f.Name!, f => (string)f.Value!);

        if (clock.GetUtcNow().ToUnixTimeMilliseconds() > long.Parse(map["e"], System.Globalization.CultureInfo.InvariantCulture))
        {
            await db.KeyDeleteAsync(key);
            return OtpVerifyResult.Expired;
        }
        var attempts = int.Parse(map["a"], System.Globalization.CultureInfo.InvariantCulture);
        if (attempts >= rules.OtpMaxAttempts)
        {
            await db.KeyDeleteAsync(key);
            return OtpVerifyResult.TooManyAttempts;
        }
        if (!hasher.Verify(code, map["h"]))
        {
            var now = await db.HashIncrementAsync(key, "a");
            if (now >= rules.OtpMaxAttempts) { await db.KeyDeleteAsync(key); return OtpVerifyResult.TooManyAttempts; }
            return OtpVerifyResult.Invalid;
        }
        return await db.KeyDeleteAsync(key) ? OtpVerifyResult.Ok : OtpVerifyResult.Invalid;   // only one caller can delete it
    }
}

/// <summary>Redis pub/sub: one instance changes the settings, all instances drop their cached copy.</summary>
public sealed class RedisSettingsInvalidationBus(RedisConnection redis) : ISettingsInvalidationBus
{
    /// <summary>Channel name.</summary>
    public string Channel => redis.Key("settings-changed");

    /// <inheritdoc />
    public async Task PublishAsync(Guid applicationId, CancellationToken ct = default) =>
        await redis.Multiplexer.GetSubscriber().PublishAsync(RedisChannel.Literal(Channel), applicationId.ToString("N"));
}

/// <summary>Without Redis there is nothing to tell: the cache of this instance is cleared directly.</summary>
public sealed class NoOpSettingsInvalidationBus : ISettingsInvalidationBus
{
    /// <inheritdoc />
    public Task PublishAsync(Guid applicationId, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Listens for settings changes of other instances.</summary>
internal sealed class SettingsInvalidationListener(RedisConnection redis, IMemoryCache cache) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var channel = RedisChannel.Literal(redis.Key("settings-changed"));
        redis.Multiplexer.GetSubscriber().Subscribe(channel, (_, value) =>
        {
            if (Guid.TryParseExact((string?)value, "N", out var id)) cache.Remove(ApplicationSettingsProvider.CacheKey(id));
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
