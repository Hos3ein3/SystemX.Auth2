using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>Resolves the application of the current request. One abstraction, two strategies (single and multi).</summary>
public interface IProjectContext
{
    /// <summary>Returns the current application. Also sets <see cref="IProjectScope"/> so query filters work.</summary>
    /// <exception cref="ProjectNotFoundException">No application matches.</exception>
    Task<Project> GetRequiredAsync(CancellationToken ct = default);
}

/// <summary>Scoped state: which application the current request works for. Read by the EF query filters.</summary>
public interface IProjectScope
{
    /// <summary>Current application id, or null when none is selected.</summary>
    Guid? ProjectId { get; }

    /// <summary>True while a global scope is active (query filters are bypassed).</summary>
    bool IsGlobalActive { get; }

    /// <summary>Selects the current application.</summary>
    void Set(Guid applicationId);

    /// <summary>Selects the current application and remembers its project key (for logs). Multi mode only; pass null in single mode.</summary>
    void Set(Guid applicationId, string? projectKey) => Set(applicationId);

    /// <summary>Project key of the current application, when known.</summary>
    string? CurrentProjectKey => null;
}

/// <summary>
/// Explicit way to bypass the per-application query filters. Only global developer code may use it
/// (seeding, global admin, background jobs).
/// </summary>
public interface IGlobalScope
{
    /// <summary>True while a global scope is active.</summary>
    bool IsActive { get; }

    /// <summary>Starts a global scope. Dispose the result to end it. Scopes can be nested.</summary>
    IDisposable Begin();
}

/// <summary>Where the client id of the current request comes from (token or authorize request).</summary>
public interface IClientIdAccessor
{
    /// <summary>The client id, or null.</summary>
    string? ClientId { get; set; }
}

/// <summary>Used by the admin API: the project an admin selected.</summary>
public interface IProjectSelection
{
    /// <summary>Selected project key, or null.</summary>
    string? ProjectKey { get; set; }
}

/// <summary>
/// The project key that names the session cookie (<c>.SystemX.Auth2.Session.{projectkey}</c>). It is set very early in the request
/// (before authentication runs) from the client id or from the only session cookie the browser sent.
/// </summary>
public interface IProjectCookieKey
{
    /// <summary>The project key for the session cookie of this request, or null when it is not known.</summary>
    string? CookieProjectKey { get; set; }
}

/// <summary>Reads cached settings of a project.</summary>
public interface IProjectSettingsProvider
{
    /// <summary>Gets the settings. Cached per application and settings version.</summary>
    Task<ProjectSettings> GetAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Removes the cached value.</summary>
    void Invalidate(Guid projectId);
}

/// <summary>Creates and updates projects.</summary>
public interface IProjectService
{
    /// <summary>Creates a project (no application). The project key never changes afterwards.</summary>
    Task<Project> CreateAsync(string projectKey, string name, string? description, CancellationToken ct = default);

    /// <summary>
    /// Creates a project and its first application, named <c>default</c>. The client id is <paramref name="clientId"/>, or the project key
    /// when it is null. The client secret is in the result and is shown only once. Used by the commands and by small setups.
    /// </summary>
    Task<ProjectCreationResult> CreateWithApplicationAsync(string projectKey, string name, string? description, string? clientId, CancellationToken ct = default);

    /// <summary>Finds a project by id (global scope).</summary>
    Task<Project?> FindAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Finds a project by project key (global scope).</summary>
    Task<Project?> FindByProjectKeyAsync(string projectKey, CancellationToken ct = default);

    /// <summary>Replaces the project settings after validation. Raises the settings version and clears the cache.</summary>
    Task UpdateSettingsAsync(Guid projectId, ProjectSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Enables or disables a project. Disabling revokes all authorizations, tokens and sessions of the project in one transaction,
    /// writes an audit event with the admin id and the reason, and keeps all data. <c>_global</c> cannot be disabled.
    /// </summary>
    Task SetEnabledAsync(Guid projectId, bool enabled, string? reason, Guid? actorUserId, CancellationToken ct = default);
}

/// <summary>A new project with its first application and the client secret (plain text, shown once).</summary>
public sealed record ProjectCreationResult(Project Project, ClientApplicationInfo Application, string ClientSecret);

/// <summary>
/// A client application as the rest of the server sees it. It is a copy of the OpenIddict application row (client id, redirect URIs,
/// scopes, consent type) and of the extra fields, so OpenIddict types stay inside the infrastructure folder.
/// </summary>
public sealed record ClientApplicationInfo(
    Guid Id,
    Guid ProjectId,
    string ClientId,
    string Name,
    bool IsEnabled,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> AllowedScopes,
    string? ConsentType,
    ClientApplicationSettings Settings,
    int SettingsVersion,
    DateTimeOffset CreatedAtUtc,
    SystemX.Auth2.Contracts.Enums.ApplicationType ApplicationType = SystemX.Auth2.Contracts.Enums.ApplicationType.Other,
    string? WebServerUrl = null);

/// <summary>What an admin gives to create an application. For an update, <c>null</c> leaves a field as it is; for <c>WebServerUrl</c> an empty text clears it.</summary>
public sealed record ApplicationSpec(
    string Name,
    string? ClientId = null,
    IReadOnlyList<string>? RedirectUris = null,
    IReadOnlyList<string>? PostLogoutRedirectUris = null,
    IReadOnlyList<string>? AllowedScopes = null,
    ClientApplicationSettings? Settings = null,
    SystemX.Auth2.Contracts.Enums.ApplicationType? ApplicationType = null,
    string? WebServerUrl = null);

/// <summary>A new application and its client secret (plain text, shown once).</summary>
public sealed record ApplicationCreationResult(ClientApplicationInfo Application, string ClientSecret);

/// <summary>The client application of the current request (from the client id of the token or the authorize request).</summary>
public interface IClientApplicationContext
{
    /// <summary>The calling application, or null when the request has no client id.</summary>
    Task<ClientApplicationInfo?> GetAsync(CancellationToken ct = default);

    /// <summary>The calling application.</summary>
    /// <exception cref="ProjectNotFoundException">No application matches.</exception>
    Task<ClientApplicationInfo> GetRequiredAsync(CancellationToken ct = default);
}

/// <summary>Creates and changes applications. Everything goes through <c>IOpenIddictApplicationManager</c>, never raw EF.</summary>
public interface IApplicationService
{
    /// <summary>Creates an application in a project. The client id is <c>{projectkey}.{name}</c> unless the spec gives one. The secret is shown once.</summary>
    Task<ApplicationCreationResult> CreateAsync(Guid projectId, ApplicationSpec spec, CancellationToken ct = default);

    /// <summary>Changes name, redirect URIs, allowed scopes and settings. Null values stay as they are.</summary>
    Task<ClientApplicationInfo> UpdateAsync(Guid applicationId, ApplicationSpec spec, CancellationToken ct = default);

    /// <summary>Enables or disables one application. Disabling revokes only the tokens and authorizations of that client.</summary>
    Task SetEnabledAsync(Guid applicationId, bool enabled, string? reason, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Deletes an application with its tokens and authorizations.</summary>
    Task DeleteAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>Makes a new client secret. The old one stops working. The new one is shown only once.</summary>
    Task<string> RotateSecretAsync(string clientId, CancellationToken ct = default);

    /// <summary>Finds an application by id (global scope).</summary>
    Task<ClientApplicationInfo?> FindAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>Finds an application by client id (global scope).</summary>
    Task<ClientApplicationInfo?> FindByClientIdAsync(string clientId, CancellationToken ct = default);

    /// <summary>Lists the applications of a project.</summary>
    Task<IReadOnlyList<ClientApplicationInfo>> ListAsync(Guid projectId, CancellationToken ct = default);
}

/// <summary>Result of a provisioning call. <c>NewClientSecret</c> is set only when the client was just created.</summary>
public sealed record ClientProvisionResult(ClientApplicationInfo Application, string? NewClientSecret);

/// <summary>
/// The only code that creates, changes or deletes OpenIddict application rows. It uses the OpenIddict manager so OpenIddict
/// validation and caching stay correct, and reads the extra fields from the same row afterwards.
/// </summary>
public interface IClientProvisioner
{
    /// <summary>Creates an application row with a new secret.</summary>
    Task<ClientProvisionResult> CreateAsync(Project project, ApplicationSpec spec, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Changes the row. Null values of the spec stay as they are. Secrets are never changed here.</summary>
    Task<ClientApplicationInfo> UpdateAsync(Guid applicationId, ProjectSettings projectSettings, ApplicationSpec spec, bool? isEnabled, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Writes the values derived from the project settings (consent type, grants) into every application of the project.</summary>
    Task ResyncProjectAsync(Guid projectId, ProjectSettings projectSettings, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Creates a new secret for the client and returns it (shown once).</summary>
    Task<string> RotateSecretAsync(string clientId, CancellationToken ct = default);

    /// <summary>Deletes the application row. Its tokens and authorizations go with it. Nothing happens when it does not exist.</summary>
    Task DeleteAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>Revokes all tokens and authorizations of one application (or of all applications of a project when <paramref name="applicationId"/> is null).</summary>
    Task<int> RevokeAsync(Guid projectId, Guid? applicationId, CancellationToken ct = default);
}

/// <summary>One audit entry. Never put secrets in it.</summary>
public sealed record AuditEntry(string Action, Guid? ProjectId = null, Guid? ActorUserId = null, string? ActorClientId = null,
    string? TargetType = null, string? TargetId = null, string? IpAddress = null, string? UserAgent = null,
    IReadOnlyDictionary<string, object?>? Data = null);

/// <summary>Writes audit events.</summary>
public interface IAuditService
{
    /// <summary>Writes an event.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
}

/// <summary>The settings are not valid.</summary>
public sealed class SettingsValidationException(IReadOnlyList<SettingsError> errors)
    : Exception("The application settings are not valid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")))
{
    /// <summary>All errors.</summary>
    public IReadOnlyList<SettingsError> Errors { get; } = errors;
}

/// <summary>No application matches the request.</summary>
public sealed class ProjectNotFoundException(string message) : Exception(message);

/// <summary>Another project already has this name (compared without letter case and without spaces around). A project name is unique on the server.</summary>
public sealed class ProjectNameTakenException(string name) : Exception($"A project named '{name}' already exists.");

/// <summary>The rule for the name of a project: it is unique on the server, compared without letter case and without spaces around.</summary>
public static class ProjectNames
{
    /// <summary>The name of the unique index on <c>lower(btrim(name))</c> of the table <c>projects</c>.</summary>
    public const string UniqueIndexName = "ux_projects_name_ci";

    /// <summary>The name as it is saved: without spaces around.</summary>
    public static string Clean(string? name) => (name ?? "").Trim();

    /// <summary>The form that is compared: lowercase, without spaces around.</summary>
    public static string Key(string? name) => Clean(name).ToLowerInvariant();

    /// <summary>True when the exception is the database refusing a second project with the same name (a race between two requests).</summary>
    public static bool IsNameConflict(Exception ex) =>
        ex is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueIndexName } };
}
