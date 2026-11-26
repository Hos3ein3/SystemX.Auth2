using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Users;

namespace SystemX.Auth2.Client.Services;

/// <summary>
/// Manages the people, roles, permissions, groups and scopes of a project on the Auth2 Server (the admin API of <c>/api/auth2/admin</c>), and the links between them.
/// Get it from <see cref="IAuth2ApiClientService.Admin"/> (an API: the token of the current request) or <see cref="Services.IAuth2WebClientService.Admin"/> (a cookie application: the
/// token of the cookie, renewed once when it is old). Both give the same calls.
/// <list type="bullet">
/// <item><description><see cref="Users"/>: create, read, change, delete, lock, sessions, and the roles, permissions and groups of a user.</description></item>
/// <item><description><see cref="Roles"/>: create, read, change, delete, and the permissions of a role.</description></item>
/// <item><description><see cref="Permissions"/>: create, read, change, mark obsolete, delete.</description></item>
/// <item><description><see cref="Groups"/>: create, read, change, delete, the roles and permissions of a group, and its members.</description></item>
/// <item><description><see cref="Scopes"/>: create, read, change, delete (GlobalAdmin changes; every admin reads).</description></item>
/// </list>
/// <para>
/// <b>Who may call.</b> A <b>signed-in admin</b> (a ProjectAdmin of this application's project, or a GlobalAdmin) whose token has the scope <c>auth2.admin</c>
/// (<see cref="Contracts.Constants.Auth2Scopes.Admin"/>): ask for it when they sign in (<c>LoginRequest.Scopes</c>) and allow it for the application. The token of the application itself
/// (client credentials) is refused with <c>forbidden</c>. No call takes a token or a <c>HttpContext</c>: it is found for you (<see cref="IAuth2ApiClientService.WithTokens"/> chooses another).
/// </para>
/// <para>
/// <b>Project.</b> The calls work on the project of this application (known from the handshake). A GlobalAdmin may work on another project with <see cref="ForProject"/>.
/// Like <see cref="IAuth2ApiClientService"/>, nothing throws for a server error: the code is in <c>Result.Error.Code</c>
/// (<c>not_found</c>, <c>forbidden</c>, <c>conflict</c>, <c>validation_failed</c>, <c>insufficient_scope</c>, <c>invalid_token</c>, ...).
/// </para>
/// <para>Ids are the ids of the lists (<c>AdminRoleDto.Id</c>, ...). A permission in a link is named by its <b>key</b>. Adding a link that exists, or removing one that does not, succeeds.</para>
/// </summary>
public interface IAuth2AdminClientService
{
    /// <summary>The same calls for another project (GlobalAdmin only; a ProjectAdmin gets <c>not_found</c>). Nothing is changed on this instance: use the returned one.</summary>
    IAuth2AdminClientService ForProject(Guid projectId);

    /// <summary>The people of the project.</summary>
    IAuth2AdminUsers Users { get; }
    /// <summary>The roles of the project.</summary>
    IAuth2AdminRoles Roles { get; }
    /// <summary>The permissions of the project.</summary>
    IAuth2AdminPermissions Permissions { get; }
    /// <summary>The groups of the project.</summary>
    IAuth2AdminGroups Groups { get; }
    /// <summary>The scopes of the server.</summary>
    IAuth2AdminScopes Scopes { get; }
}

/// <summary>The roles of a project. Roles are never renamed (their names are in tokens and policies).</summary>
public interface IAuth2AdminRoles
{
    /// <summary>Roles of the project (and the global roles on the global project) with their permission keys.</summary>
    Task<Result<PagedResult<AdminRoleDto>>> ListAsync(PageRequest? page = null, CancellationToken ct = default);
    /// <summary>One role.</summary>
    Task<Result<AdminRoleDto>> GetAsync(Guid roleId, CancellationToken ct = default);
    /// <summary>Creates a role. The names <c>GlobalAdmin</c> and <c>Developer</c> are refused (<c>forbidden</c>); a name that exists gives <c>conflict</c> or <c>validation_failed</c>.</summary>
    Task<Result<AdminRoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken ct = default);
    /// <summary>Changes the description of a role. The name never changes (<c>validation_failed</c> when another name is sent). System roles, GlobalAdmin, Developer and global roles are refused (<c>forbidden</c>).</summary>
    Task<Result> UpdateAsync(Guid roleId, UpdateRoleRequest request, CancellationToken ct = default);
    /// <summary>Deletes a role with its links. System roles, GlobalAdmin and Developer are refused (<c>forbidden</c>).</summary>
    Task<Result> DeleteAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>The permission keys of a role.</summary>
    Task<Result<IReadOnlyList<string>>> ListPermissionsAsync(Guid roleId, CancellationToken ct = default);
    /// <summary>Gives a permission to a role (every person with the role gets it).</summary>
    Task<Result> AddPermissionAsync(Guid roleId, string permissionKey, CancellationToken ct = default);
    /// <summary>Takes a permission from a role.</summary>
    Task<Result> RemovePermissionAsync(Guid roleId, string permissionKey, CancellationToken ct = default);
}

/// <summary>The permissions of a project.</summary>
public interface IAuth2AdminPermissions
{
    /// <summary>Permissions of the project.</summary>
    Task<Result<PagedResult<AdminPermissionDto>>> ListAsync(PageRequest? page = null, CancellationToken ct = default);
    /// <summary>One permission.</summary>
    Task<Result<AdminPermissionDto>> GetAsync(Guid permissionId, CancellationToken ct = default);
    /// <summary>Creates a permission (<c>invalid_permission_key</c> when the key is not valid; a key that exists is left as it is).</summary>
    Task<Result<AdminPermissionDto>> CreateAsync(PermissionDefinition request, CancellationToken ct = default);
    /// <summary>Changes the texts of a permission (display name, description, group). The key never changes. A global permission is changed in the global project only (<c>forbidden</c>).</summary>
    Task<Result> UpdateAsync(Guid permissionId, UpdatePermissionRequest request, CancellationToken ct = default);
    /// <summary>Marks a permission obsolete, or not. The grants stay.</summary>
    Task<Result> SetObsoleteAsync(Guid permissionId, bool isObsolete, CancellationToken ct = default);
    /// <summary>Deletes a permission and every grant of it.</summary>
    Task<Result> DeleteAsync(Guid permissionId, CancellationToken ct = default);
}

/// <summary>The groups of a project.</summary>
public interface IAuth2AdminGroups
{
    /// <summary>Groups of the project with their role names, permission keys and member count.</summary>
    Task<Result<PagedResult<AdminGroupDto>>> ListAsync(PageRequest? page = null, CancellationToken ct = default);
    /// <summary>One group.</summary>
    Task<Result<AdminGroupDto>> GetAsync(Guid groupId, CancellationToken ct = default);
    /// <summary>Creates a group (the name is made lowercase; <c>conflict</c> when it exists).</summary>
    Task<Result<AdminGroupDto>> CreateAsync(CreateGroupRequest request, CancellationToken ct = default);
    /// <summary>Changes the display name and the description of a group. The name never changes.</summary>
    Task<Result> UpdateAsync(Guid groupId, UpdateGroupRequest request, CancellationToken ct = default);
    /// <summary>Deletes a group with its links.</summary>
    Task<Result> DeleteAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Gives a role to every member of a group. GlobalAdmin cannot be given by a ProjectAdmin (<c>forbidden</c>).</summary>
    Task<Result> AddRoleAsync(Guid groupId, Guid roleId, CancellationToken ct = default);
    /// <summary>Takes a role from a group.</summary>
    Task<Result> RemoveRoleAsync(Guid groupId, Guid roleId, CancellationToken ct = default);
    /// <summary>Gives a permission to every member of a group.</summary>
    Task<Result> AddPermissionAsync(Guid groupId, string permissionKey, CancellationToken ct = default);
    /// <summary>Takes a permission from a group.</summary>
    Task<Result> RemovePermissionAsync(Guid groupId, string permissionKey, CancellationToken ct = default);

    /// <summary>The ids of the members of a group.</summary>
    Task<Result<IReadOnlyList<Guid>>> ListMembersAsync(Guid groupId, CancellationToken ct = default);
    /// <summary>Adds a user to a group.</summary>
    Task<Result> AddMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    /// <summary>Removes a user from a group.</summary>
    Task<Result> RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);
}

/// <summary>The scopes of the server. Every admin reads them; only a GlobalAdmin changes them (<c>forbidden</c> otherwise).</summary>
public interface IAuth2AdminScopes
{
    /// <summary>Scopes of the server.</summary>
    Task<Result<PagedResult<AdminScopeDto>>> ListAsync(PageRequest? page = null, CancellationToken ct = default);
    /// <summary>One scope.</summary>
    Task<Result<AdminScopeDto>> GetAsync(Guid scopeId, CancellationToken ct = default);
    /// <summary>Creates a scope. The name has no spaces; <c>conflict</c> when it exists.</summary>
    Task<Result<AdminScopeDto>> CreateAsync(CreateScopeRequest request, CancellationToken ct = default);
    /// <summary>Changes a scope (<c>null</c> keeps the old value).</summary>
    Task<Result<AdminScopeDto>> UpdateAsync(Guid scopeId, UpdateScopeRequest request, CancellationToken ct = default);
    /// <summary>Deletes a scope that no application uses (<c>conflict</c> otherwise).</summary>
    Task<Result> DeleteAsync(Guid scopeId, CancellationToken ct = default);
}

/// <summary>The people of a project: their account, sessions, and what they have (roles, permissions, groups).</summary>
public interface IAuth2AdminUsers
{
    /// <summary>People of the project. <paramref name="lockedOut"/> <c>true</c> gives only the locked ones, <c>false</c> only the others, <c>null</c> everybody.</summary>
    Task<Result<PagedResult<UserDto>>> ListAsync(PageRequest? page = null, bool? lockedOut = null, CancellationToken ct = default);
    /// <summary>One person.</summary>
    Task<Result<UserDto>> GetAsync(Guid userId, CancellationToken ct = default);
    /// <summary>
    /// Creates a person with a password, or, when the person already exists on the server, only adds them to this project (no password is set; see <see cref="AdminCreateUserResponse.UserWasCreated"/>).
    /// The project rules for the user name, email, phone number and password apply (<c>validation_failed</c>, <c>conflict</c>, <c>invalid_password</c>, ...).
    /// </summary>
    Task<Result<AdminCreateUserResponse>> CreateAsync(AdminCreateUserRequest request, CancellationToken ct = default);
    /// <summary>Changes the profile of a person (<c>null</c> keeps the old value; <c>IsActive</c> false turns the account off).</summary>
    Task<Result<UserDto>> UpdateAsync(Guid userId, AdminUpdateUserRequest request, CancellationToken ct = default);
    /// <summary>Removes the person from this project (their roles, groups and grants of this project go too). On a single-project server it deletes the account. Nobody deletes themselves.</summary>
    Task<Result> DeleteAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Locks a person until <paramref name="untilUtc"/>, or until an admin unlocks them (<c>null</c>).</summary>
    Task<Result> LockAsync(Guid userId, DateTimeOffset? untilUtc = null, CancellationToken ct = default);
    /// <summary>Unlocks a person.</summary>
    Task<Result> UnlockAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Turns the two-factor of a person off (they lost the authenticator and the recovery codes).</summary>
    Task<Result> ResetTwoFactorAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The open sessions of a person.</summary>
    Task<Result<IReadOnlyList<SessionDto>>> ListSessionsAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Ends one session.</summary>
    Task<Result> RevokeSessionAsync(Guid userId, string sessionId, CancellationToken ct = default);
    /// <summary>Ends every session of a person.</summary>
    Task<Result> RevokeAllSessionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>What a person has now: roles, permissions and groups (direct, and through roles and groups).</summary>
    Task<Result<CurrentUserInfo>> GetAccessAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Gives a role to a person. GlobalAdmin and Developer are given only by someone who has them (<c>forbidden</c>).</summary>
    Task<Result> AssignRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);
    /// <summary>Takes a role from a person. Nobody takes their own GlobalAdmin or ProjectAdmin role (<c>conflict</c>).</summary>
    Task<Result> RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);
    /// <summary>Makes the person a ProjectAdmin of this project (the role is created the first time).</summary>
    Task<Result> MakeProjectAdminAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Gives a permission straight to a person.</summary>
    Task<Result> GrantPermissionAsync(Guid userId, string permissionKey, CancellationToken ct = default);
    /// <summary>Takes a direct permission from a person (a permission that comes from a role or a group stays).</summary>
    Task<Result> RevokePermissionAsync(Guid userId, string permissionKey, CancellationToken ct = default);
    /// <summary>Puts a person in a group.</summary>
    Task<Result> AddToGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    /// <summary>Takes a person out of a group.</summary>
    Task<Result> RemoveFromGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}
