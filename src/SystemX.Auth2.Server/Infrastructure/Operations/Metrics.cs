using System.Diagnostics.Metrics;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>
/// The metrics of the Server (meter <c>SystemX.Auth2</c>). Counters never carry user names, emails or other personal data:
/// tags are fixed words such as the policy name or the result.
/// </summary>
public static class Auth2Metrics
{
    /// <summary>Name of the meter. Add it to OpenTelemetry with <c>AddMeter</c>.</summary>
    public const string MeterName = "SystemX.Auth2";

    private static readonly Meter Meter = new(MeterName, "1.0");

    /// <summary>Logins by result (<c>success</c> or <c>failure</c>).</summary>
    public static readonly Counter<long> Logins = Meter.CreateCounter<long>("auth2.logins", description: "Login attempts by result.");
    /// <summary>Tokens issued by grant type.</summary>
    public static readonly Counter<long> TokensIssued = Meter.CreateCounter<long>("auth2.tokens.issued", description: "Tokens issued by grant type.");
    /// <summary>Accounts locked out.</summary>
    public static readonly Counter<long> Lockouts = Meter.CreateCounter<long>("auth2.lockouts", description: "Lockouts.");
    /// <summary>Sync calls by kind (<c>permissions</c>, <c>roles</c>) and what changed.</summary>
    public static readonly Counter<long> SyncChanges = Meter.CreateCounter<long>("auth2.sync.changes", description: "Items added or updated by sync.");
    /// <summary>Requests refused by rate limiting, by policy.</summary>
    public static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>("auth2.ratelimit.rejections", description: "Requests refused by rate limiting.");
    /// <summary>Handshakes by result.</summary>
    public static readonly Counter<long> Handshakes = Meter.CreateCounter<long>("auth2.handshakes", description: "Client handshakes by result.");
}
