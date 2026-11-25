using Microsoft.AspNetCore.Http;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Options;

/// <summary>
/// Main options of the client. Section name <c>Auth2</c> when you use <c>AddAuth2Client(IConfigurationSection)</c>.
/// <c>ProjectKey</c> is set with <c>ServerWithMultiProject(projectKey)</c> and only when the server serves many projects. It is an identifier check, not a secret.
/// </summary>
public sealed class Auth2ClientOptions
{
    /// <summary>Address of the SystemX.Auth2 Server, for example <c>https://auth.example.com</c>.</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>Name of this application (the client) inside its project. The handshake checks it against the name on the server.</summary>
    public string ApplicationName { get; set; } = "";

    /// <summary>OAuth client id of this application.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>OAuth client secret. Keep it in a secret store. It is never logged.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Project key of this application on a multi-project server. Null for a single-project server (a key sent there is ignored with a warning).
    /// Set it with <c>ServerWithMultiProject(projectKey)</c>. It is not a secret and not proof of anything: the server finds the project from the client id.
    /// </summary>
    public string? ProjectKey { get; set; }

    /// <summary>What to do when the server cannot be reached at start-up. Default <see cref="StartupFailureBehavior.Continue"/>.</summary>
    public StartupFailureBehavior OnServerUnavailable { get; set; } = StartupFailureBehavior.Continue;

    /// <summary>What to do when the application is not found or the server rejects the client. Default <see cref="StartupFailureBehavior.Throw"/>.</summary>
    public StartupFailureBehavior OnProjectNotFound { get; set; } = StartupFailureBehavior.Throw;

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

    /// <summary>
    /// Send the language of the current request to the server (header <c>Accept-Language</c>, taken from <c>CultureInfo.CurrentUICulture</c>), so error messages,
    /// emails and SMS written for the call are in the language of your user. Default <c>true</c>. A culture chosen with
    /// <c>IAuth2ApiClientService.WithCulture</c> is always sent. The invariant culture sends nothing.
    /// </summary>
    public bool SendRequestCulture { get; set; } = true;

    /// <summary>Time limit of one call to the server (not the handshake). Default 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often permissions, roles and groups are read again from the server when their policy is <c>Pull</c> or <c>Both</c>. Default 10 minutes.</summary>
    public TimeSpan CatalogRefreshInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How many times a failed sync is tried again (waits grow like the handshake retries). Default 5.</summary>
    public int SyncMaxRetries { get; set; } = 5;

    /// <summary>When true, a sync that still fails after the retries stops the application. Default false: log it and carry on.</summary>
    public bool FailOnSyncError { get; set; }
}

/// <summary>Options of the machine-to-machine (client credentials) feature.</summary>
public sealed class Auth2MachineOptions
{
    /// <summary>Scopes a program asks for when it does not name any. Default: none.</summary>
    public List<string> Scopes { get; set; } = [];
}

/// <summary>Options of permission handling.</summary>
public sealed partial class Auth2PermissionOptions : CatalogOptions<SystemX.Auth2.Contracts.Access.PermissionDefinition>
{
    /// <summary>How permissions move between this application and the server. Default <see cref="PermissionSyncPolicy.None"/>.</summary>
    public PermissionSyncPolicy SyncPolicy { get; set; } = PermissionSyncPolicy.None;

    /// <summary>Where permissions come from. Default <see cref="ScanningPolicy.Manual"/>.</summary>
    public ScanningPolicy ScanningPolicy { get; set; } = ScanningPolicy.Manual;

    /// <summary>Permissions you list by hand. A shortcut: the same as <c>Add(...)</c> for each item. See also <c>AddFromClass</c>, <c>AddFromEnum</c>, <c>AddFromString</c>, <c>AddProvider</c> and the other sources.</summary>
    public List<SystemX.Auth2.Contracts.Access.PermissionDefinition> PermissionList { get; set; } = [];

    /// <summary>Assemblies to scan for attributes (used with <see cref="ScanningPolicy.AssemblyScan"/> or <see cref="ScanningPolicy.Both"/>).</summary>
    public List<System.Reflection.Assembly> AssembliesToScan { get; set; } = [];
}

/// <summary>Options of role handling.</summary>
public sealed partial class Auth2RoleOptions : CatalogOptions<SystemX.Auth2.Contracts.Access.RoleDefinition>
{
    /// <summary>How roles move between this application and the server. Default <see cref="RoleSyncPolicy.None"/>.</summary>
    public RoleSyncPolicy SyncPolicy { get; set; } = RoleSyncPolicy.None;

    /// <summary>Where roles come from. Default <see cref="ScanningPolicy.Manual"/>.</summary>
    public ScanningPolicy ScanningPolicy { get; set; } = ScanningPolicy.Manual;

    /// <summary>Roles you list by hand. A shortcut: the same as <c>Add(...)</c> for each item.</summary>
    public List<SystemX.Auth2.Contracts.Access.RoleDefinition> RoleList { get; set; } = [];

    /// <summary>Assemblies to scan for attributes.</summary>
    public List<System.Reflection.Assembly> AssembliesToScan { get; set; } = [];
}

/// <summary>Options of group handling. Pushing groups needs <c>Clients.GroupSyncAllowed</c> on the server (off by default); it only adds missing groups.</summary>
public sealed partial class Auth2GroupOptions : CatalogOptions<SystemX.Auth2.Contracts.Access.GroupDefinition>
{
    /// <summary>How groups move between this application and the server. Default <see cref="GroupSyncPolicy.None"/>.</summary>
    public GroupSyncPolicy SyncPolicy { get; set; } = GroupSyncPolicy.None;

    /// <summary>Where groups come from when they are pushed. Default <see cref="ScanningPolicy.Manual"/>.</summary>
    public ScanningPolicy ScanningPolicy { get; set; } = ScanningPolicy.Manual;

    /// <summary>Groups you list by hand. A shortcut: the same as <c>Add(...)</c> for each item.</summary>
    public List<SystemX.Auth2.Contracts.Access.GroupDefinition> GroupList { get; set; } = [];

    /// <summary>Assemblies to scan for <c>[Auth2Group]</c> attributes.</summary>
    public List<System.Reflection.Assembly> AssembliesToScan { get; set; } = [];
}

/// <summary>Options of custom scopes (for example <c>reports.read</c>). Names starting with <c>auth2.</c> and the standard OpenID Connect scopes are never sent.</summary>
public sealed partial class Auth2ScopeOptions : CatalogOptions<SystemX.Auth2.Contracts.Access.ScopeDefinition>
{
    /// <summary>How scopes move between this application and the server. Default <see cref="ScopeSyncPolicy.None"/>.</summary>
    public ScopeSyncPolicy SyncPolicy { get; set; } = ScopeSyncPolicy.None;

    /// <summary>Where scopes come from. Default <see cref="ScanningPolicy.Manual"/>.</summary>
    public ScanningPolicy ScanningPolicy { get; set; } = ScanningPolicy.Manual;

    /// <summary>Scopes you list by hand. A shortcut: the same as <c>Add(...)</c> for each item.</summary>
    public List<SystemX.Auth2.Contracts.Access.ScopeDefinition> ScopeList { get; set; } = [];

    /// <summary>Assemblies to scan for <c>[Auth2Scope]</c> attributes.</summary>
    public List<System.Reflection.Assembly> AssembliesToScan { get; set; } = [];
}

/// <summary>Options of the current-user feature.</summary>
public sealed class Auth2CurrentUserOptions
{
    /// <summary>Where roles and permissions come from. Default <see cref="CurrentUserSource.TokenAndServer"/>.</summary>
    public CurrentUserSource Source { get; set; } = CurrentUserSource.TokenAndServer;

    /// <summary>How long access data is kept before it is read again. Default 10 minutes.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Options of the sign-in cookie (<c>UseCookieAuthentication</c>, <c>UseHostedUi</c>, <c>UseSelfUi</c>).</summary>
public sealed class Auth2CookieOptions
{
    /// <summary>Cookie name. Default <c>.SystemX.Auth2.Client</c>.</summary>
    public string CookieName { get; set; } = ".SystemX.Auth2.Client";

    /// <summary>
    /// How long the cookie lasts. <b>Null (default) uses the value of the server</b> (<c>Sessions.CookieLifetimeMinutes</c>, read at the handshake),
    /// and 8 hours when the server has none.
    /// </summary>
    public TimeSpan? ExpireTimeSpan { get; set; }

    /// <summary>Renew the cookie while the user is active. Default true.</summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>SameSite mode. Default <see cref="SameSiteMode.Lax"/> (it must be Lax or None for the sign-in redirect to work).</summary>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;

    /// <summary>When the cookie is sent only over https. Default <see cref="CookieSecurePolicy.Always"/>. Use <c>SameAsRequest</c> only in development.</summary>
    public CookieSecurePolicy SecurePolicy { get; set; } = CookieSecurePolicy.Always;

    /// <summary>Where browsers go to sign in. Default <c>/auth2/login</c> (provided by the package for the hosted UI). With the Self UI, set your own page.</summary>
    public string LoginPath { get; set; } = "/auth2/login";

    /// <summary>Where signed-in users go when they may not do something. Default <c>/auth2/denied</c>.</summary>
    public string AccessDeniedPath { get; set; } = "/auth2/denied";

    /// <summary>Keep the access, refresh and identity tokens in the cookie. Default true (needed to call the server for the user's access data later).</summary>
    public bool SaveTokens { get; set; } = true;

    /// <summary>
    /// When the user's roles, permissions and groups are loaded. <see cref="AccessLoadMode.Lazy"/> (default): when first needed.
    /// <see cref="AccessLoadMode.OnSignIn"/>: at sign-in, into the store. <see cref="AccessLoadMode.RolesInCookie"/>: roles go into the cookie as claims (the cookie
    /// has a 4 KB limit: keep it for small data). <see cref="AccessLoadMode.None"/>: never.
    /// </summary>
    public AccessLoadMode AccessMode { get; set; } = AccessLoadMode.Lazy;
}

/// <summary>Options of the hosted UI (<c>UseHostedUi</c>): sign in on the Auth2 Server with the authorization code flow and PKCE.</summary>
public sealed class Auth2HostedUiOptions
{
    /// <summary>Scopes to ask for. Default: <c>openid profile email offline_access auth2.me</c>.</summary>
    public List<string> Scopes { get; set; } = ["openid", "profile", "email", "offline_access", "auth2.me"];

    /// <summary>Where the server sends the browser after sign-in. Register it as a redirect URI of the application on the server. Default <c>/signin-oidc</c>.</summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>Where the server sends the browser after sign-out. Default <c>/signout-callback-oidc</c>.</summary>
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

    /// <summary>Insist on https for the discovery address. Default: true, except when the server address is http.</summary>
    public bool? RequireHttpsMetadata { get; set; }

    /// <summary>Handler for the calls to the server (tests, proxies). Default: none.</summary>
    public HttpMessageHandler? BackchannelHandler { get; set; }
}
