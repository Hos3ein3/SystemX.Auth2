using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>Resolves the application of the current request. One abstraction, two strategies (single and multi).</summary>
public interface IApplicationContext
{
    /// <summary>Returns the current application. Also sets <see cref="IApplicationScope"/> so query filters work.</summary>
    /// <exception cref="ApplicationNotFoundException">No application matches.</exception>
    Task<Auth2Application> GetRequiredAsync(CancellationToken ct = default);
}

/// <summary>Scoped state: which application the current request works for. Read by the EF query filters.</summary>
public interface IApplicationScope
{
    /// <summary>Current application id, or null when none is selected.</summary>
    Guid? ApplicationId { get; }

    /// <summary>True while a global scope is active (query filters are bypassed).</summary>
    bool IsGlobalActive { get; }

    /// <summary>Selects the current application.</summary>
    void Set(Guid applicationId);
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
public interface IApplicationSelection
{
    /// <summary>Selected project key, or null.</summary>
    string? ProjectKey { get; set; }
}

/// <summary>Reads cached settings of an application.</summary>
public interface IApplicationSettingsProvider
{
    /// <summary>Gets the settings. Cached per application and settings version.</summary>
    Task<ApplicationSettings> GetAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>Removes the cached value.</summary>
    void Invalidate(Guid applicationId);
}

/// <summary>Creates and updates applications.</summary>
public interface IApplicationService
{
    /// <summary>Creates an application and its OpenIddict client.</summary>
    Task<Auth2Application> CreateAsync(string projectKey, string name, string? description, string? clientId, CancellationToken ct = default);

    /// <summary>Finds an application by project key (global scope).</summary>
    Task<Auth2Application?> FindByProjectKeyAsync(string projectKey, CancellationToken ct = default);

    /// <summary>Replaces the settings after validation. Raises the settings version and clears the cache.</summary>
    Task UpdateSettingsAsync(Guid applicationId, ApplicationSettings settings, CancellationToken ct = default);
}

/// <summary>Creates or updates the OpenIddict client that belongs to an application.</summary>
public interface IClientProvisioner
{
    /// <summary>Ensures the OpenIddict client exists. Returns its id.</summary>
    Task<Guid> EnsureClientAsync(Auth2Application application, CancellationToken ct = default);
}

/// <summary>One audit entry. Never put secrets in it.</summary>
public sealed record AuditEntry(string Action, Guid? ApplicationId = null, Guid? ActorUserId = null, string? ActorClientId = null,
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
public sealed class ApplicationNotFoundException(string message) : Exception(message);
