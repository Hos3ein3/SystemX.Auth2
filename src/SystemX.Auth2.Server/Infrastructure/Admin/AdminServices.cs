using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Contracts.Users;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Caching;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Admin;

internal static class AdminSupport
{
    public static string ETagOf(string? stamp) => "\"" + stamp + "\"";

    public static bool Matches(string? ifMatch, string? stamp) =>
        string.IsNullOrWhiteSpace(ifMatch) || ifMatch.Trim() == "*" || ifMatch.Trim().Trim('"') == stamp;

    public static OperationResult Invalid(IEnumerable<SettingsError> errors) => new()
    {
        ErrorCode = Auth2ErrorCodes.ValidationFailed,
        Errors = [.. errors.Select(e => new IdentityError { Code = e.Path, Description = $"{e.Path}: {e.Message}" })],
    };
}

internal sealed class AdminUserService(
    UserManager<AppUser> users, Auth2DbContext db, IAuthFlowResolver flow, UserLookup lookup, ISessionService sessions,
    IAccessVersionService versions, IAuditService audit, IOptions<Auth2ServerOptions> options, TimeProvider clock, IHttpContextAccessor http) : IAdminUserService
{
    private bool Single => options.Value.Mode == ServerMode.SingleProject;

    public async Task<PagedResult<UserDto>> ListAsync(PageQuery query, bool? lockedOut = null, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var search = query.SafeSearch?.ToUpperInvariant();
        IQueryable<AppUser> q = Single ? db.Users.AsNoTracking()
            : db.Users.AsNoTracking().Where(u => db.UserProjects.Any(x => x.UserId == u.Id && x.ProjectId == app.Id));
        if (search is not null)
            q = q.Where(u => (u.NormalizedUserName != null && u.NormalizedUserName.Contains(search)) || (u.NormalizedEmail != null && u.NormalizedEmail.Contains(search))
                || (u.PhoneNumber != null && u.PhoneNumber.Contains(search)));

        var sort = (query.Sort ?? "created").ToLowerInvariant();
        q = (sort, query.Descending) switch
        {
            ("username", false) => q.OrderBy(u => u.UserName),
            ("username", true) => q.OrderByDescending(u => u.UserName),
            ("email", false) => q.OrderBy(u => u.Email),
            ("email", true) => q.OrderByDescending(u => u.Email),
            ("created", true) or ("", true) => q.OrderByDescending(u => u.CreatedAtUtc).ThenBy(u => u.Id),
            _ => q.OrderBy(u => u.CreatedAtUtc).ThenBy(u => u.Id),
        };

        if (lockedOut is null)
        {
            var total = await q.CountAsync(ct);
            var page = await q.Skip(query.Skip).Take(query.SafePageSize).ToListAsync(ct);
            return new PagedResult<UserDto>([.. await ToDtosAsync(page, app, ct)], query.SafePage, query.SafePageSize, total);
        }
        // Lock state lives in the credential state (per application or per user), so this filter is done after loading.
        var all = await ToDtosAsync(await q.ToListAsync(ct), app, ct);
        var filtered = all.Where(u => u.IsLockedOut == lockedOut).ToList();
        return new PagedResult<UserDto>([.. filtered.Skip(query.Skip).Take(query.SafePageSize)], query.SafePage, query.SafePageSize, filtered.Count);
    }

    public async Task<AdminUserView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await MemberAsync(id, app, ct);
        return user is null ? null : new AdminUserView(await ToDtoAsync(user, app, ct), AdminSupport.ETagOf(user.ConcurrencyStamp));
    }

    public async Task<(OperationResult Result, AdminCreateUserResponse? Response)> CreateAsync(AdminCreateUserRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var email = Clean(request.Email); var phone = Clean(request.PhoneNumber); var userName = Clean(request.UserName);
        if (email is null && phone is null && userName is null) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        var name = userName ?? email ?? phone!;

        var normalizedName = users.NormalizeName(name);
        var normalizedEmail = email is null ? null : users.NormalizeEmail(email);
        var existing = await db.Users.Where(u => u.NormalizedUserName == normalizedName
            || (normalizedEmail != null && u.NormalizedEmail == normalizedEmail) || (phone != null && u.PhoneNumber == phone)).Take(3).ToListAsync(ct);
        if (existing.Count > 1) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);   // the identifiers belong to different people

        if (existing.Count == 1)
        {
            // The person exists. Only the membership is added. A password is never set here: the person uses reset or join.
            var user = existing[0];
            var joined = false;
            if (!Single && !await lookup.HasRowAsync(user, app, ct))
            {
                db.UserProjects.Add(new UserProject { UserId = user.Id, ProjectId = app.Id, JoinedAtUtc = clock.GetUtcNow(), Status = UserProjectStatus.Active });
                await db.SaveChangesAsync(ct);
                joined = true;
                await audit.WriteAsync(new AuditEntry("user.added_by_admin", app.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
            }
            return (OperationResult.Ok, new AdminCreateUserResponse(user.Id, false, joined || Single || await lookup.HasRowAsync(user, app, ct)));
        }

        var created = new AppUser { UserName = name, Email = email, PhoneNumber = phone, FirstName = request.FirstName, LastName = request.LastName, CreatedAtUtc = clock.GetUtcNow() };
        var result = string.IsNullOrEmpty(request.Password) ? await users.CreateAsync(created) : await users.CreateAsync(created, request.Password);
        if (!result.Succeeded) return (OperationResult.Fail(result.Errors), null);
        await audit.WriteAsync(new AuditEntry("user.created_by_admin", app.Id, TargetType: "user", TargetId: created.Id.ToString()), ct);
        return (OperationResult.Ok, new AdminCreateUserResponse(created.Id, true, true));
    }

    public async Task<(OperationResult Result, AdminUserView? User)> UpdateAsync(Guid id, AdminUpdateUserRequest request, string? ifMatch, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        if (!AdminSupport.Matches(ifMatch, user.ConcurrencyStamp)) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);

        if (request.FirstName is not null) user.FirstName = request.FirstName;
        if (request.LastName is not null) user.LastName = request.LastName;
        if (request.DisplayName is not null) user.DisplayName = request.DisplayName;
        if (request.PreferredCulture is not null) user.PreferredCulture = request.PreferredCulture.Length == 0 ? null : request.PreferredCulture;
        var deactivated = request.IsActive == false && user.IsActive;
        if (request.IsActive is { } active) user.IsActive = active;

        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return (OperationResult.Fail(result.Errors.Any(e => e.Code == "ConcurrencyFailure") ? [new IdentityError { Code = Auth2ErrorCodes.Conflict, Description = Auth2ErrorCodes.Conflict }] : result.Errors), null);
        if (deactivated) await sessions.RevokeAllAsync(user, app, "deactivated", null, ct);
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("user.updated", app.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return (OperationResult.Ok, new AdminUserView(await ToDtoAsync(user, app, ct), AdminSupport.ETagOf(user.ConcurrencyStamp)));
    }

    public async Task<OperationResult> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (IsActor(user)) return OperationResult.Fail(Auth2ErrorCodes.Conflict);   // not yourself
        await sessions.RevokeAllAsync(user, app, "removed", null, ct);

        if (Single)
        {
            if (await users.IsInRoleAsync(user, Auth2Roles.GlobalAdmin)) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
            var deleted = await users.DeleteAsync(user);
            if (!deleted.Succeeded) return OperationResult.Fail(deleted.Errors);
        }
        else
        {
            // Everything this person has in this application goes. The person and the other applications are not touched.
            await db.UserGroups.Where(x => x.UserId == id && db.Groups.Any(g => g.Id == x.GroupId)).ExecuteDeleteAsync(ct);
            await db.UserPermissions.Where(x => x.UserId == id).ExecuteDeleteAsync(ct);
            await db.UserRoles.Where(ur => ur.UserId == id && db.Roles.Any(r => r.Id == ur.RoleId && r.ProjectId == app.Id)).ExecuteDeleteAsync(ct);
            await db.UserProjects.Where(x => x.UserId == id && x.ProjectId == app.Id).ExecuteDeleteAsync(ct);
        }
        await audit.WriteAsync(new AuditEntry("user.removed", app.Id, TargetType: "user", TargetId: id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> LockAsync(Guid id, DateTimeOffset? untilUtc, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (IsActor(user)) return OperationResult.Fail(Auth2ErrorCodes.Conflict);
        var until = untilUtc ?? clock.GetUtcNow().AddYears(100);
        if (until <= clock.GetUtcNow()) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        await users.SetLockoutEnabledAsync(user, true);
        var result = await users.SetLockoutEndDateAsync(user, until);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await sessions.RevokeAllAsync(user, app, "locked", null, ct);
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("user.locked", app.Id, TargetType: "user", TargetId: id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> UnlockAsync(Guid id, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var result = await users.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await users.ResetAccessFailedCountAsync(user);
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("user.unlocked", app.Id, TargetType: "user", TargetId: id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<AdminBulkUserResponse> BulkActionAsync(AdminBulkUserRequest request, CancellationToken ct = default)
    {
        int ok = 0, failed = 0;
        var errors = new List<string>();
        foreach (var id in request.UserIds.Distinct().Take(500))
        {
            OperationResult result = request.Action.ToLowerInvariant() switch
            {
                "lock" => await LockAsync(id, null, ct),
                "unlock" => await UnlockAsync(id, ct),
                "reset-2fa" => await ResetTwoFactorAsync(id, ct),
                "remove" => await RemoveAsync(id, ct),
                "deactivate" => (await UpdateAsync(id, new AdminUpdateUserRequest(null, null, null, null, false), null, ct)).Result,
                "activate" => (await UpdateAsync(id, new AdminUpdateUserRequest(null, null, null, null, true), null, ct)).Result,
                _ => OperationResult.Fail(Auth2ErrorCodes.ValidationFailed),
            };
            if (result.Succeeded) ok++;
            else { failed++; errors.Add($"{id}: {result.ErrorCode}"); }
        }
        return new AdminBulkUserResponse(ok, failed, errors);
    }

    public async Task<OperationResult> ResetTwoFactorAsync(Guid id, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var off = await users.SetTwoFactorEnabledAsync(user, false);
        if (!off.Succeeded) return OperationResult.Fail(off.Errors);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);   // replaces the old codes with none
        await sessions.RevokeAllAsync(user, app, "2fa_reset", null, ct);
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("user.2fa_reset", app.Id, TargetType: "user", TargetId: id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<(OperationResult Result, IReadOnlyList<SessionDto> Sessions)> ListSessionsAsync(Guid id, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), []);
        return (OperationResult.Ok, [.. (await sessions.ListAsync(user, app, ct)).Select(s => new SessionDto(s.Id, s.CreatedAtUtc, s.Scopes))]);
    }

    public async Task<OperationResult> RevokeSessionsAsync(Guid id, string? sessionId, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (sessionId is null) await sessions.RevokeAllAsync(user, app, "admin", null, ct);
        else if (!await sessions.RevokeAsync(user, app, sessionId, ct)) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        await audit.WriteAsync(new AuditEntry("user.sessions_revoked", app.Id, TargetType: "user", TargetId: id.ToString()), ct);
        return OperationResult.Ok;
    }

    // ---------------------------------------------------------------- helpers

    private bool IsActor(AppUser user) =>
        http.HttpContext?.Items.TryGetValue(AdminItems.Actor, out var a) == true && a is Guid g && g == user.Id;

    private async Task<AppUser?> MemberAsync(Guid id, Project app, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString("D"));
        if (user is null) return null;
        return Single || await lookup.HasRowAsync(user, app, ct) ? user : null;
    }

    private async Task<List<UserDto>> ToDtosAsync(IEnumerable<AppUser> list, Project app, CancellationToken ct)
    {
        var result = new List<UserDto>();
        foreach (var u in list) result.Add(await ToDtoAsync(u, app, ct));
        return result;
    }

    private async Task<UserDto> ToDtoAsync(AppUser u, Project app, CancellationToken ct)
    {
        DateTimeOffset? lastLogin = Single ? null : await db.UserProjects.AsNoTracking()
            .Where(x => x.UserId == u.Id && x.ProjectId == app.Id).Select(x => x.LastLoginUtc).FirstOrDefaultAsync(ct);
        return new UserDto(u.Id, u.UserName, u.Email, u.EmailConfirmed, u.PhoneNumber, u.PhoneNumberConfirmed, u.FirstName, u.LastName, u.DisplayName,
            u.IsActive, await users.IsLockedOutAsync(u), await users.GetTwoFactorEnabledAsync(u), u.CreatedAtUtc, lastLogin);
    }
}

/// <summary>Keys in <c>HttpContext.Items</c> used by the admin API.</summary>
internal static class AdminItems
{
    public const string Actor = Persistence.Services.AuditService.ActorItemKey;
    public const string Context = "Auth2.AdminContext";
}

internal sealed class AdminProjectService(
    Auth2DbContext db, IGlobalScope globalScope, IProjectService projects, IClientProvisioner provisioner,
    IProjectSettingsProvider settingsProvider, IAuditService audit, IOptions<Auth2ServerOptions> options, TimeProvider clock, IHybridCacheService cache) : IAdminProjectService
{
    private bool Single => options.Value.Mode == ServerMode.SingleProject;

    public async Task<PagedResult<ProjectDto>> ListAsync(PageQuery query, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        IQueryable<Project> q = db.Projects.AsNoTracking();
        if (query.SafeSearch is { } s)
        {
            var like = $"%{s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            q = q.Where(a => EF.Functions.ILike(a.Name, like) || EF.Functions.ILike(a.ProjectKey, like));
        }
        q = ((query.Sort ?? "name").ToLowerInvariant(), query.Descending) switch
        {
            ("projectkey", false) => q.OrderBy(a => a.ProjectKey),
            ("projectkey", true) => q.OrderByDescending(a => a.ProjectKey),
            ("created", false) => q.OrderBy(a => a.CreatedAtUtc),
            ("created", true) => q.OrderByDescending(a => a.CreatedAtUtc),
            (_, true) => q.OrderByDescending(a => a.Name),
            _ => q.OrderBy(a => a.Name),
        };
        var total = await q.CountAsync(ct);
        var items = await q.Skip(query.Skip).Take(query.SafePageSize).ToListAsync(ct);
        return new PagedResult<ProjectDto>([.. items.Select(ToDto)], query.SafePage, query.SafePageSize, total);
    }

    public async Task<ProjectDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return project is null ? null : ToDto(project);
    }

    public async Task<(OperationResult Result, CreateProjectResponse? Response)> CreateAsync(CreateProjectRequest request, CancellationToken ct = default)
    {
        if (Single) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), null);
        if (!ProjectKeyRules.IsValidForNewProject(request.ProjectKey ?? "") || string.IsNullOrWhiteSpace(request.Name))
            return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        using (globalScope.Begin())
        {
            if (await db.Projects.AnyAsync(a => a.ProjectKey == request.ProjectKey, ct)) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);
        }
        try
        {
            var created = await projects.CreateAsync(request.ProjectKey!, request.Name, request.Description, ct);
            return (OperationResult.Ok, new CreateProjectResponse(ToDto(created)));
        }
        catch (ProjectNameTakenException) { return (OperationResult.Fail(Auth2ErrorCodes.ProjectNameTaken), null); }
    }

    public async Task<(OperationResult Result, ProjectDto? Project)> UpdateAsync(Guid id, UpdateProjectRequest request, Guid? actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        using var _ = globalScope.Begin();
        var project = await db.Projects.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (project is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        if (project.IsGlobalProject && !request.IsEnabled) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), null);   // never lock out the admins
        var oldName = project.Name;
        var newKey = ProjectNames.Key(request.Name);
        // The name is unique on the server: another project may not have it (this project may keep it, or only change its letter case).
        if (await db.Projects.AnyAsync(a => a.Id != id && a.Name.Trim().ToLower() == newKey, ct)) return (OperationResult.Fail(Auth2ErrorCodes.ProjectNameTaken), null);
        project.Name = ProjectNames.Clean(request.Name);
        project.Description = request.Description;
        project.UpdatedAtUtc = clock.GetUtcNow();
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (ProjectNames.IsNameConflict(ex)) { return (OperationResult.Fail(Auth2ErrorCodes.ProjectNameTaken), null); }
        cache.InvalidateProject(id);
        cache.Invalidate(AuthFlowCache.Single(oldName)); cache.Invalidate(AuthFlowCache.Single(project.Name));   // single-project mode finds its project by name
        if (project.IsEnabled != request.IsEnabled) await projects.SetEnabledAsync(id, request.IsEnabled, request.Reason, actorUserId, ct);
        await audit.WriteAsync(new AuditEntry("project.updated", project.Id, ActorUserId: actorUserId, TargetType: "project", TargetId: project.Id.ToString()), ct);
        var fresh = await db.Projects.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        return (OperationResult.Ok, ToDto(fresh));
    }

    public async Task<OperationResult> SetEnabledAsync(Guid id, bool isEnabled, string? reason, Guid? actorUserId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (project is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (project.IsGlobalProject && !isEnabled) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        await projects.SetEnabledAsync(id, isEnabled, reason, actorUserId, ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (Single) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        using var _ = globalScope.Begin();
        var project = await db.Projects.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (project is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (project.IsGlobalProject) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // The OpenIddict rows go through the manager (with their tokens and authorizations). Roles, permissions, groups and memberships go by cascade.
        foreach (var appId in await db.Set<Infrastructure.OpenIddict.Auth2Application>().Where(a => a.ProjectId == id).Select(a => a.Id).ToListAsync(ct))
            await provisioner.DeleteAsync(appId, ct);
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        settingsProvider.Invalidate(id);
        cache.InvalidateProjectCompletely(id, project.ProjectKey);
        await audit.WriteAsync(new AuditEntry("project.deleted", null, TargetType: "project", TargetId: id.ToString(),
            Data: new Dictionary<string, object?> { ["projectKey"] = project.ProjectKey }), ct);
        return OperationResult.Ok;
    }

    public async Task<(ProjectSettings Settings, int Version)?> GetSettingsAsync(Guid id, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return project is null ? null : (project.Settings, project.SettingsVersion);
    }

    public async Task<(OperationResult Result, int Version)> UpdateSettingsAsync(Guid id, ProjectSettings settings, bool isGlobalAdmin, int? ifMatchVersion, CancellationToken ct = default)
    {
        ProjectSettings current; int version;
        using (globalScope.Begin())
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (project is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), 0);
            (current, version) = (project.Settings, project.SettingsVersion);
        }
        if (ifMatchVersion is { } v && v != version) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), version);

        // The providers have their own routes (they have secrets and extra rules). This route never changes them.
        settings.ExternalProviders = current.ExternalProviders;
        settings.Messaging = current.Messaging;   // same for the messaging routes (they hold a password)

        var errors = ProjectSettingsValidator.Validate(settings);
        if (errors.Count > 0) return (AdminSupport.Invalid(errors), version);
        if (!isGlobalAdmin)
        {
            var forbidden = SettingsEditability.ChangedGlobalOnly(current, settings);
            if (forbidden.Count > 0)
                return (AdminSupport.Invalid(forbidden.Select(p => new SettingsError(p, "Only a global admin may change this setting."))) with { ErrorCode = Auth2ErrorCodes.Forbidden }, version);
        }
        await projects.UpdateSettingsAsync(id, settings, ct);
        return (OperationResult.Ok, version + 1);
    }

    private ProjectDto ToDto(Project a) =>
        new(a.Id, Single ? null : a.ProjectKey, a.Name, a.Description, a.IsEnabled, a.SettingsVersion, a.CreatedAtUtc);
}

internal sealed class AdminApplicationService(
    Auth2DbContext db, IGlobalScope globalScope, IApplicationService applications, IOptions<Auth2ServerOptions> options) : IAdminApplicationService
{
    private bool Single => options.Value.Mode == ServerMode.SingleProject;

    public async Task<IReadOnlyList<ApplicationDto>> ListAsync(Guid projectId, CancellationToken ct = default) =>
        [.. (await applications.ListAsync(projectId, ct)).Select(ToDto)];

    public async Task<ApplicationDto?> GetAsync(Guid projectId, Guid applicationId, CancellationToken ct = default)
    {
        var a = await applications.FindAsync(applicationId, ct);
        return a is null || a.ProjectId != projectId ? null : ToDto(a);   // an application of another project looks like "not found"
    }

    public async Task<(OperationResult Result, CreateApplicationResponse? Response)> CreateAsync(Guid projectId, CreateApplicationRequest request, string serverUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        string projectKey;
        using (globalScope.Begin())
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
            projectKey = project.ProjectKey;
            var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? $"{projectKey}.{request.Name.Trim().ToLowerInvariant()}" : request.ClientId.Trim();
            if (await db.Set<Infrastructure.OpenIddict.Auth2Application>().AnyAsync(a => a.ClientId == clientId || (a.ProjectId == projectId && a.Name == request.Name.Trim()), ct))
                return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);
        }
        try
        {
            var created = await applications.CreateAsync(projectId,
                new ApplicationSpec(request.Name, request.ClientId, request.RedirectUris, request.PostLogoutRedirectUris, request.AllowedScopes,
                    ApplicationType: request.ApplicationType, WebServerUrl: request.WebServerUrl), ct);
            return (OperationResult.Ok, new CreateApplicationResponse(ToDto(created.Application), created.ClientSecret,
                ClientSetup.Build(serverUrl, Single ? null : projectKey, created.Application, created.ClientSecret)));
        }
        catch (SettingsValidationException ex) { return (AdminSupport.Invalid(ex.Errors), null); }
    }

    public async Task<(OperationResult Result, ApplicationDto? Application)> UpdateAsync(Guid projectId, Guid applicationId, UpdateApplicationRequest request, Guid? actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        var current = await applications.FindAsync(applicationId, ct);
        if (current is null || current.ProjectId != projectId) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        try
        {
            var info = await applications.UpdateAsync(applicationId,
                new ApplicationSpec(request.Name, null, request.RedirectUris, request.PostLogoutRedirectUris, request.AllowedScopes,
                    ApplicationType: request.ApplicationType, WebServerUrl: request.WebServerUrl), ct);
            if (current.IsEnabled != request.IsEnabled)
            {
                await applications.SetEnabledAsync(applicationId, request.IsEnabled, request.Reason, actorUserId, ct);
                info = (await applications.FindAsync(applicationId, ct))!;
            }
            return (OperationResult.Ok, ToDto(info));
        }
        catch (SettingsValidationException ex) { return (AdminSupport.Invalid(ex.Errors), null); }
        catch (InvalidOperationException) { return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null); }
    }

    public async Task<OperationResult> DeleteAsync(Guid projectId, Guid applicationId, CancellationToken ct = default)
    {
        var current = await applications.FindAsync(applicationId, ct);
        if (current is null || current.ProjectId != projectId) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        try { await applications.DeleteAsync(applicationId, ct); }
        catch (InvalidOperationException) { return OperationResult.Fail(Auth2ErrorCodes.Forbidden); }
        return OperationResult.Ok;
    }

    public async Task<(OperationResult Result, ClientSetupDto? Setup)> RotateSecretAsync(Guid projectId, Guid applicationId, bool isGlobalAdmin, string serverUrl, CancellationToken ct = default)
    {
        var current = await applications.FindAsync(applicationId, ct);
        if (current is null || current.ProjectId != projectId) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        string projectKey; ProjectSettings settings;
        using (globalScope.Begin())
        {
            var p = await db.Projects.AsNoTracking().FirstAsync(x => x.Id == projectId, ct);
            (projectKey, settings) = (p.ProjectKey, p.Settings);
        }
        if (!isGlobalAdmin && !settings.Clients.AllowProjectAdminRotateSecret) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), null);
        var secret = await applications.RotateSecretAsync(current.ClientId, ct);
        return (OperationResult.Ok, ClientSetup.Build(serverUrl, Single ? null : projectKey, current, secret));
    }

    public async Task<(OperationResult Result, ClientSetupDto? Setup)> GetSetupAsync(Guid projectId, Guid applicationId, string serverUrl, CancellationToken ct = default)
    {
        var current = await applications.FindAsync(applicationId, ct);
        if (current is null || current.ProjectId != projectId) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        string projectKey;
        using (globalScope.Begin()) projectKey = (await db.Projects.AsNoTracking().FirstAsync(x => x.Id == projectId, ct)).ProjectKey;
        return (OperationResult.Ok, ClientSetup.Build(serverUrl, Single ? null : projectKey, current, null));
    }

    public async Task<(OperationResult Result, ClientApplicationSettings? Settings, int Version)> GetSettingsAsync(Guid projectId, Guid applicationId, CancellationToken ct = default)
    {
        var current = await applications.FindAsync(applicationId, ct);
        return current is null || current.ProjectId != projectId ? (OperationResult.Fail(Auth2ErrorCodes.NotFound), null, 0) : (OperationResult.Ok, Redacted(current.Settings), current.SettingsVersion);
    }

    public async Task<(OperationResult Result, int Version)> UpdateSettingsAsync(Guid projectId, Guid applicationId, ClientApplicationSettings settings, bool isGlobalAdmin, int? ifMatchVersion, CancellationToken ct = default)
    {
        var current = await applications.FindAsync(applicationId, ct);
        if (current is null || current.ProjectId != projectId) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), 0);
        settings.Messaging = current.Settings.Messaging;   // changed only through the messaging routes (they hold a password)
        if (ifMatchVersion is { } v && v != current.SettingsVersion) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), current.SettingsVersion);
        if (!isGlobalAdmin)
        {
            var forbidden = SettingsEditability.ChangedGlobalOnly(current.Settings, settings);
            if (forbidden.Count > 0)
                return (AdminSupport.Invalid(forbidden.Select(p => new SettingsError(p, "Only a global admin may change this setting."))) with { ErrorCode = Auth2ErrorCodes.Forbidden }, current.SettingsVersion);
        }
        try
        {
            var info = await applications.UpdateAsync(applicationId, new ApplicationSpec("", Settings: settings), ct);
            return (OperationResult.Ok, info.SettingsVersion);
        }
        catch (SettingsValidationException ex) { return (AdminSupport.Invalid(ex.Errors), current.SettingsVersion); }
    }

    private static ClientApplicationSettings Redacted(ClientApplicationSettings s)
    {
        var copy = SettingsJson.Clone(s);
        if (copy.Messaging is { } m) m.Email.PasswordProtected = null;
        return copy;
    }

    internal static ApplicationDto ToDto(ClientApplicationInfo a) =>
        new(a.Id, a.ProjectId, a.Name, a.ClientId, a.IsEnabled, a.RedirectUris, a.PostLogoutRedirectUris, a.AllowedScopes, a.SettingsVersion, a.CreatedAtUtc, a.ApplicationType, a.WebServerUrl);
}

/// <summary>Builds the copy-paste configuration of a client.</summary>
internal static class ClientSetup
{
    public static ClientSetupDto Build(string serverUrl, string? projectKey, ClientApplicationInfo app, string? secret)
    {
        var lines = new List<string> { "builder.Services.AddAuth2Client(options =>", "{" };
        lines.Add($"    options.ServerUrl = \"{serverUrl}\";");
        lines.Add($"    options.ApplicationName = \"{app.Name}\";");
        lines.Add($"    options.ClientId = \"{app.ClientId}\";");
        lines.Add($"    options.ClientSecret = \"{(secret ?? "<the client secret: shown once at creation>")}\";   // keep it in a secret store");
        lines.Add("})");
        if (projectKey is not null) lines.Add($".ServerWithMultiProject(\"{projectKey}\");"); else lines[^1] += ";";
        return new ClientSetupDto(serverUrl, projectKey, app.Name, app.ClientId, secret, string.Join(Environment.NewLine, lines));
    }
}

internal sealed class AuditReader(Auth2DbContext db, IAuthFlowResolver flow, IGlobalScope globalScope) : IAuditReader
{
    public async Task<PagedResult<AuditEventDto>> QueryAsync(PageQuery query, string? action, Guid? actorUserId, string? targetType,
        DateTimeOffset? from, DateTimeOffset? to, bool allProjects, CancellationToken ct = default)
    {
        await flow.GetProjectAsync(ct);   // selects the application for the query filter
        IDisposable? all = allProjects ? globalScope.Begin() : null;
        try
        {
            IQueryable<AuditEvent> q = db.AuditEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(action)) q = q.Where(e => e.Action == action);
            if (actorUserId is { } actor) q = q.Where(e => e.ActorUserId == actor);
            if (!string.IsNullOrWhiteSpace(targetType)) q = q.Where(e => e.TargetType == targetType);
            if (from is { } f) q = q.Where(e => e.OccurredAtUtc >= f);
            if (to is { } t) q = q.Where(e => e.OccurredAtUtc <= t);
            if (query.SafeSearch is { } s)
            {
                var sTrim = s.Trim();
                var norm = sTrim.ToUpperInvariant();
                var userMatches = await db.Users.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => u.NormalizedUserName == norm || u.NormalizedEmail == norm || (u.UserName != null && u.UserName.Contains(sTrim)) || (u.Email != null && u.Email.Contains(sTrim)))
                    .Select(u => u.Id)
                    .Take(50)
                    .ToListAsync(ct);

                if (userMatches.Count > 0)
                {
                    var userMatchStrs = userMatches.Select(id => id.ToString()).ToList();
                    q = q.Where(e => e.TargetId == sTrim 
                                  || e.Action.Contains(sTrim)
                                  || (e.ActorUserId != null && userMatches.Contains(e.ActorUserId.Value))
                                  || (e.TargetId != null && userMatchStrs.Contains(e.TargetId))
                                  || (e.DataJson != null && e.DataJson.Contains(sTrim)));
                }
                else
                {
                    q = q.Where(e => e.TargetId == sTrim || e.Action.Contains(sTrim) || (e.DataJson != null && e.DataJson.Contains(sTrim)));
                }
            }
            q = query.Descending && query.Sort is "time" ? q.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id) : q.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id);
            var total = await q.CountAsync(ct);
            var items = await q.Skip(query.Skip).Take(query.SafePageSize).ToListAsync(ct);
            return new PagedResult<AuditEventDto>([.. items.Select(e => new AuditEventDto(e.Id, e.OccurredAtUtc, e.ProjectId, e.ActorUserId, e.ActorClientId,
                e.Action, e.TargetType, e.TargetId, e.IpAddress, e.UserAgent, e.DataJson, e.ApplicationId))], query.SafePage, query.SafePageSize, total);
        }
        finally { all?.Dispose(); }
    }
}

internal sealed class AdminJobService(Auth2DbContext db, Operations.MaintenanceJobs jobs) : IAdminJobService
{
    public async Task<IReadOnlyList<JobStatusDto>> GetStatusAsync(CancellationToken ct = default)
    {
        var runs = await db.JobRuns.AsNoTracking().OrderByDescending(r => r.StartedAtUtc).Take(500).ToListAsync(ct);
        return [.. Operations.MaintenanceJobs.Names.Select(name =>
        {
            var mine = runs.Where(r => r.JobName == name).ToList();
            var last = mine.FirstOrDefault();
            return new JobStatusDto(name, last?.StartedAtUtc, mine.FirstOrDefault(r => r.Success)?.StartedAtUtc, mine.FirstOrDefault(r => !r.Success)?.Error, last?.Success ?? true);
        })];
    }

    public async Task<PagedResult<JobRunDto>> ListRunsAsync(PageQuery query, string? jobName = null, bool? success = null, CancellationToken ct = default)
    {
        IQueryable<JobRun> q = db.JobRuns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(jobName)) q = q.Where(r => r.JobName == jobName);
        if (success is { } ok) q = q.Where(r => r.Success == ok);
        q = q.OrderByDescending(r => r.StartedAtUtc);
        var total = await q.CountAsync(ct);
        var items = await q.Skip(query.Skip).Take(query.SafePageSize).ToListAsync(ct);
        return new PagedResult<JobRunDto>([.. items.Select(ToDto)], query.SafePage, query.SafePageSize, total);
    }

    public async Task<(OperationResult Result, JobRunDto? Run)> RunNowAsync(string jobName, CancellationToken ct = default)
    {
        if (!Operations.MaintenanceJobs.Names.Contains(jobName)) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        return (OperationResult.Ok, ToDto(await jobs.RunJobAsync(jobName, ct)));
    }

    private static JobRunDto ToDto(JobRun r) => new(r.Id, r.JobName, r.InstanceId, r.StartedAtUtc, r.FinishedAtUtc, r.Success, r.Error, r.RowsAffected);
}
