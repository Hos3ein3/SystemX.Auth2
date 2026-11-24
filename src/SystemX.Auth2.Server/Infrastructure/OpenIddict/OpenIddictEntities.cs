using OpenIddict.EntityFrameworkCore.Models;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.OpenIddict;

/// <summary>
/// An application: a client of a project (for example <c>papertrade-api</c>). It IS the OpenIddict application row (one table).
/// Client id, secret, client type, redirect URIs, permissions (allowed scopes) and consent type are the OpenIddict fields.
/// The extra fields below hold only what OpenIddict has no field for. Create, update and delete only through <c>IOpenIddictApplicationManager</c>.
/// </summary>
public class Auth2Application : OpenIddictEntityFrameworkCoreApplication<Guid, Auth2Authorization, Auth2Token>
{
    /// <summary>The project this client belongs to.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>False blocks only this application. Checked by the shared login gate and by the OpenIddict event handlers.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>Name, unique inside the project.</summary>
    public string Name { get; set; } = "";
    /// <summary>Client overrides (stored as jsonb).</summary>
    public ClientApplicationSettings ClientSettings { get; set; } = new();
    /// <summary>Raised on every change of the application.</summary>
    public int SettingsVersion { get; set; } = 1;
    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>Last change.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>OpenIddict authorization with a Guid key.</summary>
public class Auth2Authorization : OpenIddictEntityFrameworkCoreAuthorization<Guid, Auth2Application, Auth2Token> { }

/// <summary>OpenIddict scope with a Guid key.</summary>
public class Auth2Scope : OpenIddictEntityFrameworkCoreScope<Guid> { }

/// <summary>OpenIddict token with a Guid key.</summary>
public class Auth2Token : OpenIddictEntityFrameworkCoreToken<Guid, Auth2Application, Auth2Authorization> { }
