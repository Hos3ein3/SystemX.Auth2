using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Options;

/// <summary>
/// Main options of the client. Section name <c>Auth2</c> when you use <c>AddAuth2Client(IConfigurationSection)</c>.
/// <c>ProjectKey</c> is set with <c>ServerWithMultiApplication(projectKey)</c> and only when the server is multi-application.
/// </summary>
public sealed class Auth2ClientOptions
{
    /// <summary>Address of the SystemX.Auth2 Server, for example <c>https://auth.example.com</c>.</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>Name of this application. The handshake checks it against the name on the server.</summary>
    public string ApplicationName { get; set; } = "";

    /// <summary>OAuth client id of this application.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>OAuth client secret. Keep it in a secret store. It is never logged.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Project key of this application on a multi-application server. Null for a single-application server.
    /// Set it with <c>ServerWithMultiApplication(projectKey)</c>.
    /// </summary>
    public string? ProjectKey { get; set; }

    /// <summary>What to do when the server cannot be reached at start-up. Default <see cref="StartupFailureBehavior.Continue"/>.</summary>
    public StartupFailureBehavior OnServerUnavailable { get; set; } = StartupFailureBehavior.Continue;

    /// <summary>What to do when the application is not found or the server rejects the client. Default <see cref="StartupFailureBehavior.Throw"/>.</summary>
    public StartupFailureBehavior OnApplicationNotFound { get; set; } = StartupFailureBehavior.Throw;

    /// <summary>Make the handshake when the application starts. Default true.</summary>
    public bool HandshakeOnStartup { get; set; } = true;

    /// <summary>Time limit of one handshake attempt. Default 5 seconds.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many background retries after an unreachable server (only with <see cref="StartupFailureBehavior.Continue"/>). Default 6.</summary>
    public int HandshakeMaxRetries { get; set; } = 6;

    /// <summary>Wait before the first retry. It doubles each time. Default 2 seconds.</summary>
    public TimeSpan HandshakeInitialDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Longest wait between two retries. Default 60 seconds.</summary>
    public TimeSpan HandshakeMaxDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Version text sent in the handshake. Default: the version of this package.</summary>
    public string ClientVersion { get; set; } = typeof(Auth2ClientOptions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Time limit of one call to the server (not the handshake). Default 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Options of the machine-to-machine (client credentials) feature.</summary>
public sealed class Auth2MachineOptions
{
    /// <summary>Scopes a program asks for when it does not name any. Default: none.</summary>
    public List<string> Scopes { get; set; } = [];
}

/// <summary>Options of permission handling.</summary>
public sealed class Auth2PermissionOptions
{
    /// <summary>How permissions move between this application and the server. Default <see cref="PermissionSyncPolicy.None"/>.</summary>
    public PermissionSyncPolicy SyncPolicy { get; set; } = PermissionSyncPolicy.None;

    /// <summary>Where permissions come from. Default <see cref="ScanningPolicy.Manual"/>.</summary>
    public ScanningPolicy ScanningPolicy { get; set; } = ScanningPolicy.Manual;

    /// <summary>Permissions you list by hand.</summary>
    public List<SystemX.Auth2.Contracts.Access.PermissionDefinition> PermissionList { get; set; } = [];

    /// <summary>Assemblies to scan for attributes (used with <see cref="ScanningPolicy.AssemblyScan"/> or <see cref="ScanningPolicy.Both"/>).</summary>
    public List<System.Reflection.Assembly> AssembliesToScan { get; set; } = [];
}

/// <summary>Options of role handling.</summary>
public sealed class Auth2RoleOptions
{
    /// <summary>How roles move between this application and the server. Default <see cref="RoleSyncPolicy.None"/>.</summary>
    public RoleSyncPolicy SyncPolicy { get; set; } = RoleSyncPolicy.None;

    /// <summary>Where roles come from. Default <see cref="ScanningPolicy.Manual"/>.</summary>
    public ScanningPolicy ScanningPolicy { get; set; } = ScanningPolicy.Manual;

    /// <summary>Roles you list by hand.</summary>
    public List<SystemX.Auth2.Contracts.Access.RoleDefinition> RoleList { get; set; } = [];

    /// <summary>Assemblies to scan for attributes.</summary>
    public List<System.Reflection.Assembly> AssembliesToScan { get; set; } = [];
}

/// <summary>Options of group handling. Groups are decided by admins, so they can only be read.</summary>
public sealed class Auth2GroupOptions
{
    /// <summary>Default <see cref="GroupSyncPolicy.None"/>.</summary>
    public GroupSyncPolicy SyncPolicy { get; set; } = GroupSyncPolicy.None;
}

/// <summary>Options of the current-user feature.</summary>
public sealed class Auth2CurrentUserOptions
{
    /// <summary>Where roles and permissions come from. Default <see cref="CurrentUserSource.TokenAndServer"/>.</summary>
    public CurrentUserSource Source { get; set; } = CurrentUserSource.TokenAndServer;

    /// <summary>How long access data is kept before it is read again. Default 10 minutes.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(10);
}
