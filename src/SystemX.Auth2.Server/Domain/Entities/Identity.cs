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
    /// <summary>Optional free JSON profile (jsonb).</summary>
    public string? Profile { get; set; }
}

/// <summary>A role. <c>ApplicationId</c> is null for a global role.</summary>
public class AppRole : IdentityRole<Guid>
{
    /// <summary>Creates a role with a new time-ordered id.</summary>
    public AppRole() => Id = Guid.CreateVersion7();

    /// <summary>Creates a role with a name.</summary>
    public AppRole(string name) : this() => Name = name;

    /// <summary>Owner application, or null for a global role.</summary>
    public Guid? ApplicationId { get; set; }
    /// <summary>Description.</summary>
    public string? Description { get; set; }
    /// <summary>True for built-in roles that cannot be deleted.</summary>
    public bool IsSystem { get; set; }
}

/// <summary>External login. <c>ApplicationId</c> makes it per application when set.</summary>
public class AppUserLogin : IdentityUserLogin<Guid>
{
    /// <summary>Optional application.</summary>
    public Guid? ApplicationId { get; set; }
}

/// <summary>User token. <c>ApplicationId</c> makes it per application when set.</summary>
public class AppUserToken : IdentityUserToken<Guid>
{
    /// <summary>Optional application.</summary>
    public Guid? ApplicationId { get; set; }
}
