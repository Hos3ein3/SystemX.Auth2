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
