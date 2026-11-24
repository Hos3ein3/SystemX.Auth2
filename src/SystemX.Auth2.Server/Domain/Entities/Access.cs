namespace SystemX.Auth2.Server.Domain.Entities;

/// <summary>How a permission was created.</summary>
public enum PermissionSource
{
    /// <summary>Created by an admin.</summary>
    Manual = 0,
    /// <summary>Created by client sync.</summary>
    Sync = 1,
}

/// <summary>A permission such as <c>orders.read</c>. Unique per application.</summary>
public class Permission
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Project id.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>The application that pushed it by sync, or null (created by an admin).</summary>
    public Guid? ApplicationId { get; set; }
    /// <summary>Lowercase key.</summary>
    public string Key { get; set; } = "";
    /// <summary>Display name.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Description.</summary>
    public string? Description { get; set; }
    /// <summary>Group label for screens.</summary>
    public string? Group { get; set; }
    /// <summary>Source.</summary>
    public PermissionSource Source { get; set; }
    /// <summary>Marked as no longer used.</summary>
    public bool IsObsolete { get; set; }
}

/// <summary>Direct permission grant to a user.</summary>
public class UserPermission
{
    /// <summary>User id.</summary>
    public Guid UserId { get; set; }
    /// <summary>Permission id.</summary>
    public Guid PermissionId { get; set; }
    /// <summary>Application id.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>When granted.</summary>
    public DateTimeOffset GrantedAtUtc { get; set; }
    /// <summary>Who granted it.</summary>
    public Guid? GrantedBy { get; set; }
}

/// <summary>Permission of a role.</summary>
public class RolePermission
{
    /// <summary>Role id.</summary>
    public Guid RoleId { get; set; }
    /// <summary>Permission id.</summary>
    public Guid PermissionId { get; set; }
}

/// <summary>A group of users. Name is unique per application.</summary>
public class Group
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Project id.</summary>
    public Guid ProjectId { get; set; }
    /// <summary>The application that pushed it by sync, or null.</summary>
    public Guid? ApplicationId { get; set; }
    /// <summary>Name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Display name.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Description.</summary>
    public string? Description { get; set; }
}

/// <summary>User is a member of a group.</summary>
public class UserGroup
{
    /// <summary>User id.</summary>
    public Guid UserId { get; set; }
    /// <summary>Group id.</summary>
    public Guid GroupId { get; set; }
}

/// <summary>Group has a role.</summary>
public class GroupRole
{
    /// <summary>Group id.</summary>
    public Guid GroupId { get; set; }
    /// <summary>Role id.</summary>
    public Guid RoleId { get; set; }
}

/// <summary>Group has a permission.</summary>
public class GroupPermission
{
    /// <summary>Group id.</summary>
    public Guid GroupId { get; set; }
    /// <summary>Permission id.</summary>
    public Guid PermissionId { get; set; }
}
