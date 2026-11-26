using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Users;

namespace SystemX.Auth2.Client.Services;

/// <summary>How an admin call reaches the Server: with the token of the current request (<see cref="Auth2ApiClientService"/>) or with the token of the cookie (the web service).</summary>
internal interface IAuth2AdminTransport
{
    Task<Result> SendAsync(HttpMethod method, string path, object? body, bool idempotent, CancellationToken ct);
    Task<Result<T>> SendAsync<T>(HttpMethod method, string path, object? body, bool idempotent, CancellationToken ct);
    /// <summary>The id of the project of this application (from the handshake), or null.</summary>
    Task<Guid?> ProjectIdAsync(CancellationToken ct);
}

/// <summary>The admin calls. The routes are the constants of <see cref="Auth2AdminRoutes"/>.</summary>
internal sealed class Auth2AdminClientService : IAuth2AdminClientService
{
    private readonly IAuth2AdminTransport _transport;
    private readonly Guid? _projectId;

    public Auth2AdminClientService(IAuth2AdminTransport transport, Guid? projectId)
    {
        _transport = transport;
        _projectId = projectId;
        Users = new UsersPart(this);
        Roles = new RolesPart(this);
        Permissions = new PermissionsPart(this);
        Groups = new GroupsPart(this);
        Scopes = new ScopesPart(this);
    }

    public IAuth2AdminClientService ForProject(Guid projectId) => new Auth2AdminClientService(_transport, projectId);
    public IAuth2AdminUsers Users { get; }
    public IAuth2AdminRoles Roles { get; }
    public IAuth2AdminPermissions Permissions { get; }
    public IAuth2AdminGroups Groups { get; }
    public IAuth2AdminScopes Scopes { get; }

    // ---------------------------------------------------------------- plumbing shared by the parts

    internal static string Fill(string route, params (string Name, object Value)[] values) => Auth2AdminRoutes.Fill(route, values);

    internal static string Query(PageRequest? p, string extra = "")
    {
        var query = p is null ? "" : $"page={p.Page}&pageSize={p.PageSize}" + (string.IsNullOrWhiteSpace(p.Search) ? "" : "&q=" + Uri.EscapeDataString(p.Search)) + (p.Descending ? "&desc=true" : "");
        if (extra.Length > 0) query = query.Length == 0 ? extra : query + "&" + extra;
        return query.Length == 0 ? "" : "?" + query;
    }

    internal static Result<IReadOnlyList<T>> AsList<T>(Result<List<T>> r) =>
        r.IsSuccess ? Result<IReadOnlyList<T>>.Success(r.Value ?? []) : Result<IReadOnlyList<T>>.Failure(r.Error!.Code, r.Error.Message);

    internal async Task<Result<T>> ReadAsync<T>(string route, CancellationToken ct) => await SendAsync<T>(HttpMethod.Get, route, null, true, ct);
    internal Task<Result<T>> WriteAsync<T>(HttpMethod method, string route, object? body, CancellationToken ct) => SendAsync<T>(method, route, body, false, ct);
    internal Task<Result> WriteAsync(HttpMethod method, string route, object? body, CancellationToken ct) => SendAsync(method, route, body, false, ct);

    private async Task<Result<T>> SendAsync<T>(HttpMethod method, string route, object? body, bool idempotent, CancellationToken ct)
    {
        var (project, failure) = await ProjectAsync(ct);
        return project is null ? Result<T>.Failure(failure!.Error!.Code, failure.Error.Message) : await _transport.SendAsync<T>(method, Auth2AdminRoutes.ForProject(project.Value, route), body, idempotent, ct);
    }

    private async Task<Result> SendAsync(HttpMethod method, string route, object? body, bool idempotent, CancellationToken ct)
    {
        var (project, failure) = await ProjectAsync(ct);
        return project is null ? failure! : await _transport.SendAsync(method, Auth2AdminRoutes.ForProject(project.Value, route), body, idempotent, ct);
    }

    private async Task<(Guid? Project, Result? Failure)> ProjectAsync(CancellationToken ct)
    {
        var id = _projectId ?? await _transport.ProjectIdAsync(ct);
        return id is { } known ? (known, null) : (null, Result.Failure(Auth2ErrorCodes.NotFound, "The project of this application is not known yet: the handshake has not worked."));
    }

    // ---------------------------------------------------------------- the parts

    private sealed class RolesPart(Auth2AdminClientService a) : IAuth2AdminRoles
    {
        public Task<Result<PagedResult<AdminRoleDto>>> ListAsync(PageRequest? page = null, CancellationToken ct = default) => a.ReadAsync<PagedResult<AdminRoleDto>>(Auth2AdminRoutes.Roles + Query(page), ct);
        public Task<Result<AdminRoleDto>> GetAsync(Guid roleId, CancellationToken ct = default) => a.ReadAsync<AdminRoleDto>(Fill(Auth2AdminRoutes.Role, ("id", roleId)), ct);
        public Task<Result<AdminRoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken ct = default) => a.WriteAsync<AdminRoleDto>(HttpMethod.Post, Auth2AdminRoutes.Roles, request, ct);
        public Task<Result> UpdateAsync(Guid roleId, UpdateRoleRequest request, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.Role, ("id", roleId)), request, ct);
        public Task<Result> DeleteAsync(Guid roleId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.Role, ("id", roleId)), null, ct);
        public async Task<Result<IReadOnlyList<string>>> ListPermissionsAsync(Guid roleId, CancellationToken ct = default) => AsList(await a.ReadAsync<List<string>>(Fill(Auth2AdminRoutes.RolePermissions, ("id", roleId)), ct));
        public Task<Result> AddPermissionAsync(Guid roleId, string permissionKey, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.RolePermission, ("id", roleId), ("key", permissionKey)), null, ct);
        public Task<Result> RemovePermissionAsync(Guid roleId, string permissionKey, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.RolePermission, ("id", roleId), ("key", permissionKey)), null, ct);
    }

    private sealed class PermissionsPart(Auth2AdminClientService a) : IAuth2AdminPermissions
    {
        public Task<Result<PagedResult<AdminPermissionDto>>> ListAsync(PageRequest? page = null, CancellationToken ct = default) => a.ReadAsync<PagedResult<AdminPermissionDto>>(Auth2AdminRoutes.Permissions + Query(page), ct);
        public Task<Result<AdminPermissionDto>> GetAsync(Guid permissionId, CancellationToken ct = default) => a.ReadAsync<AdminPermissionDto>(Fill(Auth2AdminRoutes.Permission, ("id", permissionId)), ct);
        public Task<Result<AdminPermissionDto>> CreateAsync(PermissionDefinition request, CancellationToken ct = default) => a.WriteAsync<AdminPermissionDto>(HttpMethod.Post, Auth2AdminRoutes.Permissions, request, ct);
        public Task<Result> UpdateAsync(Guid permissionId, UpdatePermissionRequest request, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.Permission, ("id", permissionId)), request, ct);
        public Task<Result> SetObsoleteAsync(Guid permissionId, bool isObsolete, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.PermissionObsolete, ("id", permissionId)), new SetObsoleteRequest(isObsolete), ct);
        public Task<Result> DeleteAsync(Guid permissionId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.Permission, ("id", permissionId)), null, ct);
    }

    private sealed class GroupsPart(Auth2AdminClientService a) : IAuth2AdminGroups
    {
        public Task<Result<PagedResult<AdminGroupDto>>> ListAsync(PageRequest? page = null, CancellationToken ct = default) => a.ReadAsync<PagedResult<AdminGroupDto>>(Auth2AdminRoutes.Groups + Query(page), ct);
        public Task<Result<AdminGroupDto>> GetAsync(Guid groupId, CancellationToken ct = default) => a.ReadAsync<AdminGroupDto>(Fill(Auth2AdminRoutes.Group, ("id", groupId)), ct);
        public Task<Result<AdminGroupDto>> CreateAsync(CreateGroupRequest request, CancellationToken ct = default) => a.WriteAsync<AdminGroupDto>(HttpMethod.Post, Auth2AdminRoutes.Groups, request, ct);
        public Task<Result> UpdateAsync(Guid groupId, UpdateGroupRequest request, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.Group, ("id", groupId)), request, ct);
        public Task<Result> DeleteAsync(Guid groupId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.Group, ("id", groupId)), null, ct);
        public Task<Result> AddRoleAsync(Guid groupId, Guid roleId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.GroupRole, ("id", groupId), ("roleId", roleId)), null, ct);
        public Task<Result> RemoveRoleAsync(Guid groupId, Guid roleId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.GroupRole, ("id", groupId), ("roleId", roleId)), null, ct);
        public Task<Result> AddPermissionAsync(Guid groupId, string permissionKey, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.GroupPermission, ("id", groupId), ("key", permissionKey)), null, ct);
        public Task<Result> RemovePermissionAsync(Guid groupId, string permissionKey, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.GroupPermission, ("id", groupId), ("key", permissionKey)), null, ct);
        public async Task<Result<IReadOnlyList<Guid>>> ListMembersAsync(Guid groupId, CancellationToken ct = default) => AsList(await a.ReadAsync<List<Guid>>(Fill(Auth2AdminRoutes.GroupMembers, ("id", groupId)), ct));
        public Task<Result> AddMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.GroupMember, ("id", groupId), ("userId", userId)), null, ct);
        public Task<Result> RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.GroupMember, ("id", groupId), ("userId", userId)), null, ct);
    }

    private sealed class ScopesPart(Auth2AdminClientService a) : IAuth2AdminScopes
    {
        public Task<Result<PagedResult<AdminScopeDto>>> ListAsync(PageRequest? page = null, CancellationToken ct = default) => a.ReadAsync<PagedResult<AdminScopeDto>>(Auth2AdminRoutes.Scopes + Query(page), ct);
        public Task<Result<AdminScopeDto>> GetAsync(Guid scopeId, CancellationToken ct = default) => a.ReadAsync<AdminScopeDto>(Fill(Auth2AdminRoutes.Scope, ("id", scopeId)), ct);
        public Task<Result<AdminScopeDto>> CreateAsync(CreateScopeRequest request, CancellationToken ct = default) => a.WriteAsync<AdminScopeDto>(HttpMethod.Post, Auth2AdminRoutes.Scopes, request, ct);
        public Task<Result<AdminScopeDto>> UpdateAsync(Guid scopeId, UpdateScopeRequest request, CancellationToken ct = default) => a.WriteAsync<AdminScopeDto>(HttpMethod.Put, Fill(Auth2AdminRoutes.Scope, ("id", scopeId)), request, ct);
        public Task<Result> DeleteAsync(Guid scopeId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.Scope, ("id", scopeId)), null, ct);
    }

    private sealed class UsersPart(Auth2AdminClientService a) : IAuth2AdminUsers
    {
        public Task<Result<PagedResult<UserDto>>> ListAsync(PageRequest? page = null, bool? lockedOut = null, CancellationToken ct = default) =>
            a.ReadAsync<PagedResult<UserDto>>(Auth2AdminRoutes.Users + Query(page, lockedOut is { } l ? "lockedOut=" + (l ? "true" : "false") : ""), ct);
        public Task<Result<UserDto>> GetAsync(Guid userId, CancellationToken ct = default) => a.ReadAsync<UserDto>(Fill(Auth2AdminRoutes.User, ("id", userId)), ct);
        public Task<Result<AdminCreateUserResponse>> CreateAsync(AdminCreateUserRequest request, CancellationToken ct = default) => a.WriteAsync<AdminCreateUserResponse>(HttpMethod.Post, Auth2AdminRoutes.Users, request, ct);
        public Task<Result<UserDto>> UpdateAsync(Guid userId, AdminUpdateUserRequest request, CancellationToken ct = default) => a.WriteAsync<UserDto>(HttpMethod.Put, Fill(Auth2AdminRoutes.User, ("id", userId)), request, ct);
        public Task<Result> DeleteAsync(Guid userId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.User, ("id", userId)), null, ct);

        public Task<Result> LockAsync(Guid userId, DateTimeOffset? untilUtc = null, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Post, Fill(Auth2AdminRoutes.UserLock, ("id", userId)), new LockUserRequest(untilUtc), ct);
        public Task<Result> UnlockAsync(Guid userId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Post, Fill(Auth2AdminRoutes.UserUnlock, ("id", userId)), null, ct);
        public Task<Result> ResetTwoFactorAsync(Guid userId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Post, Fill(Auth2AdminRoutes.UserReset2Fa, ("id", userId)), null, ct);

        public async Task<Result<IReadOnlyList<SessionDto>>> ListSessionsAsync(Guid userId, CancellationToken ct = default) => AsList(await a.ReadAsync<List<SessionDto>>(Fill(Auth2AdminRoutes.UserSessions, ("id", userId)), ct));
        public Task<Result> RevokeSessionAsync(Guid userId, string sessionId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.UserSession, ("id", userId), ("sessionId", sessionId)), null, ct);
        public Task<Result> RevokeAllSessionsAsync(Guid userId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.UserSessions, ("id", userId)), null, ct);

        public Task<Result<CurrentUserInfo>> GetAccessAsync(Guid userId, CancellationToken ct = default) => a.ReadAsync<CurrentUserInfo>(Fill(Auth2AdminRoutes.UserAccess, ("id", userId)), ct);
        public Task<Result> AssignRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.UserRole, ("id", userId), ("roleId", roleId)), null, ct);
        public Task<Result> RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.UserRole, ("id", userId), ("roleId", roleId)), null, ct);
        public Task<Result> MakeProjectAdminAsync(Guid userId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.UserProjectAdmin, ("id", userId)), null, ct);
        public Task<Result> GrantPermissionAsync(Guid userId, string permissionKey, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.UserPermission, ("id", userId), ("key", permissionKey)), null, ct);
        public Task<Result> RevokePermissionAsync(Guid userId, string permissionKey, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.UserPermission, ("id", userId), ("key", permissionKey)), null, ct);
        public Task<Result> AddToGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Put, Fill(Auth2AdminRoutes.UserGroup, ("id", userId), ("groupId", groupId)), null, ct);
        public Task<Result> RemoveFromGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default) => a.WriteAsync(HttpMethod.Delete, Fill(Auth2AdminRoutes.UserGroup, ("id", userId), ("groupId", groupId)), null, ct);
    }
}
