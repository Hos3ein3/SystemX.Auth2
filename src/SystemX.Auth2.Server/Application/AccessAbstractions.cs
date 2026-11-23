using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Users;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Application;

/// <summary>What a user may do in the current application. Lists are sorted and have no duplicates.</summary>
/// <param name="Roles">Own roles plus roles of the user's groups.</param>
/// <param name="Permissions">Direct permissions, permissions of the roles above, and permissions of the user's groups.</param>
/// <param name="Groups">Names of the user's groups.</param>
/// <param name="AccessVersion">The access version (application version + user version). It only grows.</param>
public sealed record EffectiveAccess(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Groups, int AccessVersion);

/// <summary>
/// Reads and changes access data (permissions, roles, role permissions, direct grants) of the <b>current application</b>.
/// Every change that can alter someone's access raises the access version, so cached data and the <c>pv</c> claim refresh.
/// </summary>
public interface IAccessService
{
    /// <summary>The access version of a user in the current application. Cheap: one small query.</summary>
    Task<int> GetAccessVersionAsync(AppUser user, CancellationToken ct = default);

    /// <summary>Effective access. Cached by (application, user, access version).</summary>
    Task<EffectiveAccess> GetEffectiveAccessAsync(AppUser user, CancellationToken ct = default);

    /// <summary>Builds the <c>/me</c> answer.</summary>
    Task<CurrentUserInfo> GetCurrentUserAsync(AppUser user, IEnumerable<string> scopes, CancellationToken ct = default);

    /// <summary>True when the user has the permission (key is normalized first).</summary>
    Task<bool> HasPermissionAsync(AppUser user, string permissionKey, CancellationToken ct = default);

    /// <summary>All permissions of the application.</summary>
    Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken ct = default);

    /// <summary>Creates a permission (manual). Succeeds without change when it already exists.</summary>
    Task<OperationResult> CreatePermissionAsync(PermissionDefinition definition, CancellationToken ct = default);

    /// <summary>Marks a permission as obsolete (or not). Grants stay.</summary>
    Task<OperationResult> SetPermissionObsoleteAsync(string permissionKey, bool obsolete, CancellationToken ct = default);

    /// <summary>Deletes a permission and every grant of it.</summary>
    Task<OperationResult> DeletePermissionAsync(string permissionKey, CancellationToken ct = default);

    /// <summary>Grants a permission directly to a user. Succeeds without change when already granted.</summary>
    Task<OperationResult> GrantPermissionAsync(AppUser user, string permissionKey, Guid? grantedBy = null, CancellationToken ct = default);

    /// <summary>Removes a direct grant.</summary>
    Task<OperationResult> RevokePermissionAsync(AppUser user, string permissionKey, CancellationToken ct = default);

    /// <summary>All roles visible in the application with their permissions.</summary>
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default);

    /// <summary>Creates a role in the current application.</summary>
    Task<OperationResult> CreateRoleAsync(string name, string? description = null, CancellationToken ct = default);

    /// <summary>Deletes a role of the current application (not a system role).</summary>
    Task<OperationResult> DeleteRoleAsync(string name, CancellationToken ct = default);

    /// <summary>Gives a role to a user.</summary>
    Task<OperationResult> AssignRoleAsync(AppUser user, string roleName, CancellationToken ct = default);

    /// <summary>Takes a role from a user.</summary>
    Task<OperationResult> RemoveRoleAsync(AppUser user, string roleName, CancellationToken ct = default);

    /// <summary>Adds a permission to a role. The permission must exist.</summary>
    Task<OperationResult> AddRolePermissionAsync(string roleName, string permissionKey, CancellationToken ct = default);

    /// <summary>Removes a permission from a role.</summary>
    Task<OperationResult> RemoveRolePermissionAsync(string roleName, string permissionKey, CancellationToken ct = default);
}

/// <summary>Groups of the current application: members, roles and permissions of a group.</summary>
public interface IGroupService
{
    /// <summary>All groups.</summary>
    Task<IReadOnlyList<GroupDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates a group. The name is lowercase, unique per application.</summary>
    Task<OperationResult> CreateAsync(string name, string? displayName = null, string? description = null, CancellationToken ct = default);

    /// <summary>Deletes a group with its links.</summary>
    Task<OperationResult> DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>Adds a user to a group.</summary>
    Task<OperationResult> AddUserAsync(string groupName, AppUser user, CancellationToken ct = default);

    /// <summary>Removes a user from a group.</summary>
    Task<OperationResult> RemoveUserAsync(string groupName, AppUser user, CancellationToken ct = default);

    /// <summary>Gives a role to every member of the group.</summary>
    Task<OperationResult> AddRoleAsync(string groupName, string roleName, CancellationToken ct = default);

    /// <summary>Takes a role from the group.</summary>
    Task<OperationResult> RemoveRoleAsync(string groupName, string roleName, CancellationToken ct = default);

    /// <summary>Gives a permission to every member of the group.</summary>
    Task<OperationResult> AddPermissionAsync(string groupName, string permissionKey, CancellationToken ct = default);

    /// <summary>Takes a permission from the group.</summary>
    Task<OperationResult> RemovePermissionAsync(string groupName, string permissionKey, CancellationToken ct = default);
}
