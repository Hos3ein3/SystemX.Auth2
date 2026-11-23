using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Contracts.Users;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>Rules for permission keys and group names. No regular expression: nothing here can hang.</summary>
public static class AccessKeys
{
    /// <summary>Longest permission key.</summary>
    public const int MaxPermissionKeyLength = 100;

    /// <summary>
    /// Trims and lower-cases a key. Valid keys are 1–100 characters of <c>a-z 0-9 . _ : -</c> that start and end with a letter or digit
    /// (for example <c>orders.read</c>). Returns null when the key is not valid.
    /// </summary>
    public static string? NormalizePermissionKey(string? key)
    {
        var k = key?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(k) || k.Length > MaxPermissionKeyLength) return null;
        if (!IsAlnum(k[0]) || !IsAlnum(k[^1])) return null;
        foreach (var c in k) if (!IsAlnum(c) && c is not ('.' or '_' or ':' or '-')) return null;
        return k;
    }

    /// <summary>Group names follow the same rules as permission keys (lowercase, max 100).</summary>
    public static string? NormalizeGroupName(string? name) => NormalizePermissionKey(name);

    /// <summary>Scopes that clients may not register: the OpenID Connect standard ones.</summary>
    private static readonly HashSet<string> StandardScopes = new(StringComparer.Ordinal) { "openid", "profile", "email", "phone", "roles", "offline_access" };

    /// <summary>
    /// True for scopes that belong to the server: <c>auth2.*</c> and the standard OpenID Connect scopes.
    /// A client cannot create or change these through sync.
    /// </summary>
    public static bool IsReservedScope(string name) => name.StartsWith("auth2.", StringComparison.Ordinal) || StandardScopes.Contains(name);

    /// <summary>Trims a scope name (the same characters as permission keys, 1–100) and returns null when it is invalid or reserved.</summary>
    public static string? NormalizeScopeName(string? name)
    {
        var n = NormalizePermissionKey(name);
        return n is null || IsReservedScope(n) ? null : n;
    }

    private static bool IsAlnum(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>Shared helpers for the access services.</summary>
internal static class AccessSupport
{
    public static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction that holds a per-application advisory lock, so two syncs of one application
    /// run one after the other (no deadlocks between their inserts). The lock is released at commit or rollback.
    /// </summary>
    public static Task<T> SerializedAsync<T>(Auth2DbContext db, Guid applicationId, Func<Task<T>> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var lockKey = "auth2.sync:" + applicationId.ToString("N");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });

    /// <summary>Global roles are visible in the global project and in single-application mode.</summary>
    public static bool IncludeGlobalRoles(Auth2Application app, ServerMode mode) => app.IsGlobalProject || mode == ServerMode.SingleApplication;
}

/// <summary>
/// Effective access and access management for the current application.
/// Effective permissions = direct grants ∪ permissions of the user's roles ∪ permissions of the roles of the user's groups ∪ permissions of the user's groups.
/// Effective roles = own roles ∪ roles of the user's groups.
/// </summary>
internal sealed class AccessService(
    Auth2DbContext db, IAuthFlowResolver flow, RoleManager<AppRole> roles, UserManager<AppUser> users,
    IAccessVersionService versions, IMemoryCache cache, IAuditService audit, TimeProvider clock, IOptions<Auth2ServerOptions> options) : IAccessService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private ServerMode Mode => options.Value.Mode;

    // ---------------------------------------------------------------- reading

    public async Task<int> GetAccessVersionAsync(AppUser user, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        // Read the application's version from the database every time, so another instance's change is seen at once.
        var appVersion = await db.Applications.AsNoTracking().Where(a => a.Id == app.Id).Select(a => a.AccessVersion).FirstAsync(ct);
        if (Mode != ServerMode.MultiApplication) return appVersion;
        var userVersion = await db.UserApplications.AsNoTracking().Where(x => x.UserId == user.Id && x.ApplicationId == app.Id)
            .Select(x => (int?)x.UserAccessVersion).FirstOrDefaultAsync(ct);
        return appVersion + (userVersion ?? 0);
    }

    public async Task<EffectiveAccess> GetEffectiveAccessAsync(AppUser user, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var version = await GetAccessVersionAsync(user, ct);
        var key = $"auth2:access:{app.Id:N}:{user.Id:N}:{version}";
        if (cache.TryGetValue(key, out EffectiveAccess? hit) && hit is not null) return hit;

        var access = await LoadAsync(user, app, version, ct);
        cache.Set(key, access, CacheLifetime);
        return access;
    }

    private async Task<EffectiveAccess> LoadAsync(AppUser user, Auth2Application app, int version, CancellationToken ct)
    {
        if (Mode == ServerMode.MultiApplication && !await db.UserApplications.AsNoTracking().AnyAsync(x => x.UserId == user.Id && x.ApplicationId == app.Id, ct))
            return new EffectiveAccess([], [], [], version, [], []);   // not a member of this application: no access

        var includeGlobal = AccessSupport.IncludeGlobalRoles(app, Mode);
        var groups = await (from ug in db.UserGroups.AsNoTracking()
                            join g in db.Groups.AsNoTracking() on ug.GroupId equals g.Id
                            where ug.UserId == user.Id
                            select new { g.Id, g.Name }).ToListAsync(ct);
        var groupIds = groups.Select(g => g.Id).ToList();

        var directRoleIds = db.UserRoles.AsNoTracking().Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId);
        var groupRoleIds = db.GroupRoles.AsNoTracking().Where(gr => groupIds.Contains(gr.GroupId)).Select(gr => gr.RoleId);
        var roleRows = await db.Roles.AsNoTracking()
            .Where(r => (directRoleIds.Contains(r.Id) || groupRoleIds.Contains(r.Id)) && (r.ApplicationId == app.Id || (includeGlobal && r.ApplicationId == null)))
            .Select(r => new { r.Id, r.Name }).ToListAsync(ct);
        var roleIds = roleRows.Select(r => r.Id).ToList();

        var direct = db.UserPermissions.AsNoTracking().Where(p => p.UserId == user.Id).Select(p => p.PermissionId);
        var viaRoles = db.RolePermissions.AsNoTracking().Where(p => roleIds.Contains(p.RoleId)).Select(p => p.PermissionId);
        var viaGroups = db.GroupPermissions.AsNoTracking().Where(p => groupIds.Contains(p.GroupId)).Select(p => p.PermissionId);
        var keys = await db.Permissions.AsNoTracking()
            .Where(p => direct.Contains(p.Id) || viaRoles.Contains(p.Id) || viaGroups.Contains(p.Id))
            .Select(p => p.Key).ToListAsync(ct);

        var directKeys = await db.Permissions.AsNoTracking().Where(p => direct.Contains(p.Id)).Select(p => p.Key).ToListAsync(ct);
        var roleKeys = await db.Permissions.AsNoTracking().Where(p => viaRoles.Contains(p.Id)).Select(p => p.Key).ToListAsync(ct);
        return new EffectiveAccess(Sorted(roleRows.Select(r => r.Name!)), Sorted(keys), Sorted(groups.Select(g => g.Name)), version, Sorted(directKeys), Sorted(roleKeys));
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> values) => [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public async Task<CurrentUserInfo> GetCurrentUserAsync(AppUser user, IEnumerable<string> scopes, CancellationToken ct = default)
    {
        var access = await GetEffectiveAccessAsync(user, ct);
        var full = $"{user.FirstName} {user.LastName}".Trim();
        return new CurrentUserInfo(user.Id, user.UserName, user.Email, user.PhoneNumber, user.FirstName, user.LastName,
            !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : (full.Length > 0 ? full : user.UserName),
            access.Roles, access.Permissions, access.Groups, [.. scopes.Order(StringComparer.Ordinal)], access.AccessVersion,
            access.DirectPermissions ?? [], access.RolePermissions ?? []);
    }

    public async Task<bool> HasPermissionAsync(AppUser user, string permissionKey, CancellationToken ct = default)
    {
        var key = AccessKeys.NormalizePermissionKey(permissionKey);
        return key is not null && (await GetEffectiveAccessAsync(user, ct)).Permissions.Contains(key);
    }

    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken ct = default)
    {
        await flow.GetApplicationAsync(ct);
        return await db.Permissions.AsNoTracking().OrderBy(p => p.Key)
            .Select(p => new PermissionDto(p.Key, p.DisplayName, p.Description, p.Group, p.IsObsolete)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var includeGlobal = AccessSupport.IncludeGlobalRoles(app, Mode);
        var rows = await db.Roles.AsNoTracking().Where(r => r.ApplicationId == app.Id || (includeGlobal && r.ApplicationId == null))
            .OrderBy(r => r.Name).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var perms = await (from rp in db.RolePermissions.AsNoTracking()
                           join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                           where ids.Contains(rp.RoleId)
                           select new { rp.RoleId, p.Key }).ToListAsync(ct);
        return [.. rows.Select(r => new RoleDto(r.Name!, r.Description, r.ApplicationId is null, r.IsSystem,
            Sorted(perms.Where(p => p.RoleId == r.Id).Select(p => p.Key))))];
    }

    // ---------------------------------------------------------------- permissions

    public async Task<OperationResult> CreatePermissionAsync(PermissionDefinition definition, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var key = AccessKeys.NormalizePermissionKey(definition.Key);
        if (key is null) return OperationResult.Fail(Auth2ErrorCodes.InvalidPermissionKey);
        if (await db.Permissions.AnyAsync(p => p.Key == key, ct)) return OperationResult.Ok;

        db.Permissions.Add(new Permission
        {
            ApplicationId = app.Id, Key = key, DisplayName = definition.DisplayName, Description = definition.Description,
            Group = definition.Group, Source = PermissionSource.Manual,
        });
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (AccessSupport.IsUniqueViolation(ex)) { db.ChangeTracker.Clear(); return OperationResult.Ok; }   // someone created it at the same time
        await Audit("permission.created", app, "permission", key, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> SetPermissionObsoleteAsync(string permissionKey, bool obsolete, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var permission = await FindPermissionAsync(permissionKey, ct);
        if (permission is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (permission.IsObsolete == obsolete) return OperationResult.Ok;
        permission.IsObsolete = obsolete;
        await db.SaveChangesAsync(ct);
        await Audit(obsolete ? "permission.obsolete" : "permission.restored", app, "permission", permission.Key, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeletePermissionAsync(string permissionKey, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var permission = await FindPermissionAsync(permissionKey, ct);
        if (permission is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        db.Permissions.Remove(permission);   // links are removed by the cascade
        await db.SaveChangesAsync(ct);
        await versions.BumpApplicationAsync(ct);
        await Audit("permission.deleted", app, "permission", permission.Key, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> GrantPermissionAsync(AppUser user, string permissionKey, Guid? grantedBy = null, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var permission = await FindPermissionAsync(permissionKey, ct);
        if (permission is null) return OperationResult.Fail(AccessKeys.NormalizePermissionKey(permissionKey) is null ? Auth2ErrorCodes.InvalidPermissionKey : Auth2ErrorCodes.NotFound);
        if (!await IsMemberAsync(user, app, ct)) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (await db.UserPermissions.AnyAsync(x => x.UserId == user.Id && x.PermissionId == permission.Id, ct)) return OperationResult.Ok;

        db.UserPermissions.Add(new UserPermission
        {
            UserId = user.Id, PermissionId = permission.Id, ApplicationId = app.Id, GrantedAtUtc = clock.GetUtcNow(), GrantedBy = grantedBy,
        });
        if (!await TrySaveAsync(ct)) return OperationResult.Ok;
        await versions.BumpUserAsync(user, ct);
        await Audit("permission.granted", app, "user", user.Id.ToString(), ct, ("permission", permission.Key));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> RevokePermissionAsync(AppUser user, string permissionKey, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var permission = await FindPermissionAsync(permissionKey, ct);
        if (permission is null) return OperationResult.Fail(AccessKeys.NormalizePermissionKey(permissionKey) is null ? Auth2ErrorCodes.InvalidPermissionKey : Auth2ErrorCodes.NotFound);
        var removed = await db.UserPermissions.Where(x => x.UserId == user.Id && x.PermissionId == permission.Id).ExecuteDeleteAsync(ct);
        if (removed == 0) return OperationResult.Ok;
        await versions.BumpUserAsync(user, ct);
        await Audit("permission.revoked", app, "user", user.Id.ToString(), ct, ("permission", permission.Key));
        return OperationResult.Ok;
    }

    // ---------------------------------------------------------------- roles

    public async Task<OperationResult> CreateRoleAsync(string name, string? description = null, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var result = await roles.CreateAsync(new AppRole(name.Trim()) { ApplicationId = app.Id, Description = description });
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await Audit("role.created", app, "role", name.Trim(), ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteRoleAsync(string name, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var role = await FindVisibleRoleAsync(name, app);
        if (role is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (role.IsSystem) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        var result = await roles.DeleteAsync(role);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await versions.BumpApplicationAsync(ct);
        await Audit("role.deleted", app, "role", role.Name!, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> AssignRoleAsync(AppUser user, string roleName, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var role = await FindVisibleRoleAsync(roleName, app);
        if (role is null || !await IsMemberAsync(user, app, ct)) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (await users.IsInRoleAsync(user, role.Name!)) return OperationResult.Ok;
        var result = await users.AddToRoleAsync(user, role.Name!);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);   // the user store raised the access version
        await Audit("role.assigned", app, "user", user.Id.ToString(), ct, ("role", role.Name));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> RemoveRoleAsync(AppUser user, string roleName, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var role = await FindVisibleRoleAsync(roleName, app);
        if (role is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (!await users.IsInRoleAsync(user, role.Name!)) return OperationResult.Ok;
        var result = await users.RemoveFromRoleAsync(user, role.Name!);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);   // the user store raised the access version
        await Audit("role.removed", app, "user", user.Id.ToString(), ct, ("role", role.Name));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> AddRolePermissionAsync(string roleName, string permissionKey, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var role = await FindVisibleRoleAsync(roleName, app);
        var permission = await FindPermissionAsync(permissionKey, ct);
        if (role is null || permission is null) return OperationResult.Fail(AccessKeys.NormalizePermissionKey(permissionKey) is null ? Auth2ErrorCodes.InvalidPermissionKey : Auth2ErrorCodes.NotFound);
        if (await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id, ct)) return OperationResult.Ok;
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        if (!await TrySaveAsync(ct)) return OperationResult.Ok;
        await versions.BumpApplicationAsync(ct);
        await Audit("role.permission_added", app, "role", role.Name!, ct, ("permission", permission.Key));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> RemoveRolePermissionAsync(string roleName, string permissionKey, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var role = await FindVisibleRoleAsync(roleName, app);
        var permission = await FindPermissionAsync(permissionKey, ct);
        if (role is null || permission is null) return OperationResult.Fail(AccessKeys.NormalizePermissionKey(permissionKey) is null ? Auth2ErrorCodes.InvalidPermissionKey : Auth2ErrorCodes.NotFound);
        var removed = await db.RolePermissions.Where(x => x.RoleId == role.Id && x.PermissionId == permission.Id).ExecuteDeleteAsync(ct);
        if (removed == 0) return OperationResult.Ok;
        await versions.BumpApplicationAsync(ct);
        await Audit("role.permission_removed", app, "role", role.Name!, ct, ("permission", permission.Key));
        return OperationResult.Ok;
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Permission?> FindPermissionAsync(string key, CancellationToken ct)
    {
        var normalized = AccessKeys.NormalizePermissionKey(key);
        return normalized is null ? null : await db.Permissions.FirstOrDefaultAsync(p => p.Key == normalized, ct);
    }

    private async Task<AppRole?> FindVisibleRoleAsync(string name, Auth2Application app)
    {
        var role = await roles.FindByNameAsync(name.Trim());
        if (role is null) return null;
        return role.ApplicationId == app.Id || (role.ApplicationId is null && AccessSupport.IncludeGlobalRoles(app, Mode)) ? role : null;
    }

    private async Task<bool> IsMemberAsync(AppUser user, Auth2Application app, CancellationToken ct) =>
        Mode != ServerMode.MultiApplication || await db.UserApplications.AnyAsync(x => x.UserId == user.Id && x.ApplicationId == app.Id, ct);

    /// <summary>Saves. Returns false when a parallel request already did the same insert (unique violation).</summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (Exception ex) when (AccessSupport.IsUniqueViolation(ex)) { db.ChangeTracker.Clear(); return false; }
    }

    private Task Audit(string action, Auth2Application app, string targetType, string targetId, CancellationToken ct, (string, object?)? extra = null) =>
        audit.WriteAsync(new AuditEntry(action, app.Id, TargetType: targetType, TargetId: targetId,
            Data: extra is { } e ? new Dictionary<string, object?> { [e.Item1] = e.Item2 } : null), ct);
}

internal sealed class GroupService(Auth2DbContext db, IAuthFlowResolver flow, RoleManager<AppRole> roles, IAccessVersionService versions,
    IAuditService audit, IOptions<Auth2ServerOptions> options) : IGroupService
{
    public async Task<IReadOnlyList<GroupDto>> ListAsync(CancellationToken ct = default)
    {
        await flow.GetApplicationAsync(ct);
        var groups = await db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var roleRows = await (from gr in db.GroupRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on gr.RoleId equals r.Id
                              where ids.Contains(gr.GroupId) select new { gr.GroupId, r.Name }).ToListAsync(ct);
        var permRows = await (from gp in db.GroupPermissions.AsNoTracking() join p in db.Permissions.AsNoTracking() on gp.PermissionId equals p.Id
                              where ids.Contains(gp.GroupId) select new { gp.GroupId, p.Key }).ToListAsync(ct);
        return [.. groups.Select(g => new GroupDto(g.Name, g.DisplayName, g.Description,
            [.. roleRows.Where(r => r.GroupId == g.Id).Select(r => r.Name!).Order(StringComparer.Ordinal)],
            [.. permRows.Where(p => p.GroupId == g.Id).Select(p => p.Key).Order(StringComparer.Ordinal)]))];
    }

    public async Task<OperationResult> CreateAsync(string name, string? displayName = null, string? description = null, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var normalized = AccessKeys.NormalizeGroupName(name);
        if (normalized is null) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        if (await db.Groups.AnyAsync(g => g.Name == normalized, ct)) return OperationResult.Fail(Auth2ErrorCodes.Conflict);
        db.Groups.Add(new Group { ApplicationId = app.Id, Name = normalized, DisplayName = displayName, Description = description });
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (AccessSupport.IsUniqueViolation(ex)) { db.ChangeTracker.Clear(); return OperationResult.Fail(Auth2ErrorCodes.Conflict); }
        await Audit("group.created", app, normalized, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(string name, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var group = await FindAsync(name, ct);
        if (group is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        db.Groups.Remove(group);   // links are removed by the cascade
        await db.SaveChangesAsync(ct);
        await versions.BumpApplicationAsync(ct);
        await Audit("group.deleted", app, group.Name, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> AddUserAsync(string groupName, AppUser user, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var group = await FindAsync(groupName, ct);
        if (group is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (options.Value.Mode == ServerMode.MultiApplication && !await db.UserApplications.AnyAsync(x => x.UserId == user.Id && x.ApplicationId == app.Id, ct))
            return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (await db.UserGroups.AnyAsync(x => x.UserId == user.Id && x.GroupId == group.Id, ct)) return OperationResult.Ok;
        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        if (!await TrySaveAsync(ct)) return OperationResult.Ok;
        await versions.BumpUserAsync(user, ct);
        await Audit("group.user_added", app, group.Name, ct, ("userId", user.Id.ToString()));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> RemoveUserAsync(string groupName, AppUser user, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var group = await FindAsync(groupName, ct);
        if (group is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (await db.UserGroups.Where(x => x.UserId == user.Id && x.GroupId == group.Id).ExecuteDeleteAsync(ct) == 0) return OperationResult.Ok;
        await versions.BumpUserAsync(user, ct);
        await Audit("group.user_removed", app, group.Name, ct, ("userId", user.Id.ToString()));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> AddRoleAsync(string groupName, string roleName, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var group = await FindAsync(groupName, ct);
        var role = await FindRoleAsync(roleName, app);
        if (group is null || role is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (await db.GroupRoles.AnyAsync(x => x.GroupId == group.Id && x.RoleId == role.Id, ct)) return OperationResult.Ok;
        db.GroupRoles.Add(new GroupRole { GroupId = group.Id, RoleId = role.Id });
        if (!await TrySaveAsync(ct)) return OperationResult.Ok;
        await versions.BumpApplicationAsync(ct);
        await Audit("group.role_added", app, group.Name, ct, ("role", role.Name));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> RemoveRoleAsync(string groupName, string roleName, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var group = await FindAsync(groupName, ct);
        var role = await FindRoleAsync(roleName, app);
        if (group is null || role is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (await db.GroupRoles.Where(x => x.GroupId == group.Id && x.RoleId == role.Id).ExecuteDeleteAsync(ct) == 0) return OperationResult.Ok;
        await versions.BumpApplicationAsync(ct);
        await Audit("group.role_removed", app, group.Name, ct, ("role", role.Name));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> AddPermissionAsync(string groupName, string permissionKey, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var group = await FindAsync(groupName, ct);
        var permission = await FindPermissionAsync(permissionKey, ct);
        if (group is null || permission is null) return OperationResult.Fail(AccessKeys.NormalizePermissionKey(permissionKey) is null ? Auth2ErrorCodes.InvalidPermissionKey : Auth2ErrorCodes.NotFound);
        if (await db.GroupPermissions.AnyAsync(x => x.GroupId == group.Id && x.PermissionId == permission.Id, ct)) return OperationResult.Ok;
        db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionId = permission.Id });
        if (!await TrySaveAsync(ct)) return OperationResult.Ok;
        await versions.BumpApplicationAsync(ct);
        await Audit("group.permission_added", app, group.Name, ct, ("permission", permission.Key));
        return OperationResult.Ok;
    }

    public async Task<OperationResult> RemovePermissionAsync(string groupName, string permissionKey, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var group = await FindAsync(groupName, ct);
        var permission = await FindPermissionAsync(permissionKey, ct);
        if (group is null || permission is null) return OperationResult.Fail(AccessKeys.NormalizePermissionKey(permissionKey) is null ? Auth2ErrorCodes.InvalidPermissionKey : Auth2ErrorCodes.NotFound);
        if (await db.GroupPermissions.Where(x => x.GroupId == group.Id && x.PermissionId == permission.Id).ExecuteDeleteAsync(ct) == 0) return OperationResult.Ok;
        await versions.BumpApplicationAsync(ct);
        await Audit("group.permission_removed", app, group.Name, ct, ("permission", permission.Key));
        return OperationResult.Ok;
    }

    private async Task<Group?> FindAsync(string name, CancellationToken ct)
    {
        var normalized = AccessKeys.NormalizeGroupName(name);
        return normalized is null ? null : await db.Groups.FirstOrDefaultAsync(g => g.Name == normalized, ct);
    }

    private async Task<Permission?> FindPermissionAsync(string key, CancellationToken ct)
    {
        var normalized = AccessKeys.NormalizePermissionKey(key);
        return normalized is null ? null : await db.Permissions.FirstOrDefaultAsync(p => p.Key == normalized, ct);
    }

    private async Task<AppRole?> FindRoleAsync(string name, Auth2Application app)
    {
        var role = await roles.FindByNameAsync(name.Trim());
        if (role is null) return null;
        return role.ApplicationId == app.Id || (role.ApplicationId is null && AccessSupport.IncludeGlobalRoles(app, options.Value.Mode)) ? role : null;
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (Exception ex) when (AccessSupport.IsUniqueViolation(ex)) { db.ChangeTracker.Clear(); return false; }
    }

    private Task Audit(string action, Auth2Application app, string group, CancellationToken ct, (string, object?)? extra = null) =>
        audit.WriteAsync(new AuditEntry(action, app.Id, TargetType: "group", TargetId: group,
            Data: extra is { } e ? new Dictionary<string, object?> { [e.Item1] = e.Item2 } : null), ct);
}
