using Microsoft.AspNetCore.Identity;

namespace SystemX.Auth2.Server.Domain.Entities;

/// <summary>One real person. User name and email are unique across the whole server.</summary>
public class AppUser : IdentityUser<Guid>
{
    /// <summary>Creates a user with a new time-ordered id.</summary>
    public AppUser() => Id = Guid.CreateVersion7();

    /// <summary>First name.</summary>
    public string? FirstName { get; set; }
    /// <summary>Last name.</summary>
    public string? LastName { get; set; }
    /// <summary>Display name.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Saved culture, for example <c>fa</c>.</summary>
    public string? PreferredCulture { get; set; }
    /// <summary>False when an admin disabled the person everywhere.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>When the password was last set (shared credentials only; per-project credentials keep it in <c>user_projects</c>).</summary>
    public DateTimeOffset? PasswordChangedAtUtc { get; set; }
    /// <summary>Optional free JSON profile (jsonb).</summary>
    public string? Profile { get; set; }
}

/// <summary>A role. <c>ProjectId</c> is null for a global role.</summary>
public class AppRole : IdentityRole<Guid>
{
    /// <summary>Creates a role with a new time-ordered id.</summary>
    public AppRole() => Id = Guid.CreateVersion7();

    /// <summary>Creates a role with a name.</summary>
    public AppRole(string name) : this() => Name = name;

    /// <summary>Owner project, or null for a global role.</summary>
    public Guid? ProjectId { get; set; }
    /// <summary>The application that pushed it by sync, or null.</summary>
    public Guid? ApplicationId { get; set; }
    /// <summary>Description.</summary>
    public string? Description { get; set; }
    /// <summary>True for built-in roles that cannot be deleted.</summary>
    public bool IsSystem { get; set; }
}

/// <summary>
/// A link between a user and an external identity (social, OIDC, SAML or LDAP). Always per application:
/// the same Google account in two applications gives two rows. <c>LoginProvider</c> is the provider <b>name</b> inside the application,
/// <c>ProviderKey</c> is the stable id at the provider (never a user name or DN).
/// </summary>
public class AppUserLogin : IdentityUserLogin<Guid>
{
    /// <summary>The application of this link.</summary>
    public Guid ProjectId { get; set; }
}

/// <summary>A passkey (WebAuthn credential). Per application: a passkey of one application is never found in another.</summary>
public class AppUserPasskey : IdentityUserPasskey<Guid>
{
    /// <summary>The application of this passkey.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>Authenticator model id (AAGUID), read at registration. Used by the block list.</summary>
    public Guid? Aaguid { get; set; }
}

/// <summary>User token. <c>ProjectId</c> makes it per application when set.</summary>
public class AppUserToken : IdentityUserToken<Guid>
{
    /// <summary>Optional application.</summary>
    public Guid? ProjectId { get; set; }
}
