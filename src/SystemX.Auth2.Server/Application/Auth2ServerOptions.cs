using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Server.Application;

/// <summary>Server options. Section <c>Auth2</c>.</summary>
public sealed class Auth2ServerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth2";

    /// <summary>Server mode. Use <c>ServerWithSingleApplication()</c> to set single mode.</summary>
    public ServerMode Mode { get; set; } = ServerMode.MultiApplication;

    /// <summary>Server-wide credential scope.</summary>
    public CredentialScope CredentialScope { get; set; } = CredentialScope.PerApplication;

    /// <summary>When true, an application may override the credential scope in its settings.</summary>
    public bool AllowCredentialScopeOverride { get; set; }

    /// <summary>Public issuer URL. Needed in Production (HTTPS).</summary>
    public string? Issuer { get; set; }

    /// <summary>Run the seeder at startup outside Development.</summary>
    public bool AutoSeed { get; set; }

    /// <summary>Run migrations at startup in Production.</summary>
    public bool AutoMigrate { get; set; }

    /// <summary>Single-application options.</summary>
    public SingleApplicationOptions SingleApplication { get; set; } = new();

    /// <summary>Options that only work in Development.</summary>
    public DevelopmentOptions Development { get; set; } = new();

    /// <summary>Redis. Optional. Section <c>Auth2:Redis</c>.</summary>
    public RedisOptions Redis { get; set; } = new();

    /// <summary>Rate limiting. Section <c>Auth2:RateLimiting</c>.</summary>
    public RateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>HTTP hardening. Section <c>Auth2:Security</c>.</summary>
    public SecurityOptions Security { get; set; } = new();

    /// <summary>Background jobs. Section <c>Auth2:Jobs</c>.</summary>
    public JobsOptions Jobs { get; set; } = new();

    /// <summary>External identity providers. Section <c>Auth2:ExternalProviders</c>.</summary>
    public ExternalProviderOptions ExternalProviders { get; set; } = new();
}

/// <summary>How the Server keeps its list of external providers fresh.</summary>
public sealed class ExternalProviderOptions
{
    /// <summary>How often every instance looks for changed provider settings, in seconds. Default 30. Redis makes changes visible at once; the poll is the safety net.</summary>
    public int PollSeconds { get; set; } = 30;
}

/// <summary>Redis settings. Redis is on when <see cref="ConnectionString"/> is set.</summary>
public sealed class RedisOptions
{
    /// <summary>StackExchange.Redis connection string. Keep it in a secret store.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Prefix of every key and channel. Lets several servers share one Redis.</summary>
    public string KeyPrefix { get; set; } = "auth2";

    /// <summary>True when Redis is configured.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(ConnectionString);
}

/// <summary>Limit of one named rate-limit policy: at most <see cref="PermitLimit"/> requests per <see cref="WindowSeconds"/>.</summary>
public sealed class RateLimitRule
{
    /// <summary>Requests allowed in the window.</summary>
    public int PermitLimit { get; set; }
    /// <summary>Window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Rate limiting. Policies: login, token, register, otp, password-reset, handshake, permission-sync, admin.</summary>
public sealed class RateLimitingOptions
{
    /// <summary>Names of the policies.</summary>
    public static readonly string[] PolicyNames = ["login", "token", "register", "otp", "password-reset", "handshake", "permission-sync", "admin"];

    /// <summary>Master switch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Overrides per policy name. Missing policies use <see cref="Default"/> values.</summary>
    public Dictionary<string, RateLimitRule> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The rule of a policy (configured, else the built-in default).</summary>
    public RateLimitRule RuleFor(string policy) =>
        Policies.TryGetValue(policy, out var r) && r.PermitLimit > 0 ? r : Default(policy);

    /// <summary>Built-in defaults. Per client address and login or client id.</summary>
    public static RateLimitRule Default(string policy) => policy switch
    {
        "login" => new RateLimitRule { PermitLimit = 10, WindowSeconds = 60 },
        "token" => new RateLimitRule { PermitLimit = 60, WindowSeconds = 60 },
        "register" => new RateLimitRule { PermitLimit = 10, WindowSeconds = 60 },
        "otp" => new RateLimitRule { PermitLimit = 10, WindowSeconds = 60 },
        "password-reset" => new RateLimitRule { PermitLimit = 5, WindowSeconds = 60 },
        "handshake" => new RateLimitRule { PermitLimit = 30, WindowSeconds = 60 },
        "permission-sync" => new RateLimitRule { PermitLimit = 30, WindowSeconds = 60 },
        _ => new RateLimitRule { PermitLimit = 120, WindowSeconds = 60 },   // admin
    };
}

/// <summary>HTTP hardening. Section <c>Auth2:Security</c>.</summary>
public sealed class SecurityOptions
{
    /// <summary>Redirect HTTP to HTTPS and send HSTS. Default: on outside Development and Testing.</summary>
    public bool? RequireHttps { get; set; }

    /// <summary>Read <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from a reverse proxy. Turn on behind a proxy.</summary>
    public bool UseForwardedHeaders { get; set; }

    /// <summary>Trust forwarded headers from any proxy. Only for a network where nobody else can reach the server. Otherwise list <see cref="KnownProxies"/>.</summary>
    public bool TrustAllProxies { get; set; }

    /// <summary>Addresses of trusted proxies.</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>Value of <c>Content-Security-Policy</c>. A strict placeholder: the UI team sets the real policy.</summary>
    public string ContentSecurityPolicy { get; set; } = "default-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
}

/// <summary>Background jobs. Section <c>Auth2:Jobs</c>. One instance runs each job at a time (a database lock).</summary>
public sealed class JobsOptions
{
    /// <summary>Master switch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes between runs.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Audit events older than this many days are deleted. 0 keeps them forever.</summary>
    public int AuditRetentionDays { get; set; } = 365;
}

/// <summary>Development helpers. Section <c>Auth2:Development</c>. They are ignored outside Development.</summary>
public sealed class DevelopmentOptions
{
    /// <summary>
    /// The development email and SMS senders log the full message (links and codes) when this is true.
    /// That puts secrets in the log, so it works only in Development. Default: false (only a masked recipient and the subject are logged).
    /// </summary>
    public bool LogMessageBodies { get; set; }
}

/// <summary>Options for single-application mode. Section <c>Auth2:SingleApplication</c>.</summary>
public sealed class SingleApplicationOptions
{
    /// <summary>Name of the one fixed application.</summary>
    public string Name { get; set; } = "Default";
}
