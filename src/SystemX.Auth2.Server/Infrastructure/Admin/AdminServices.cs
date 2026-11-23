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
    private bool Single => options.Value.Mode == ServerMode.SingleApplication;

    public async Task<PagedResult<UserDto>> ListAsync(PageQuery query, bool? lockedOut = null, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var search = query.SafeSearch?.ToUpperInvariant();
        IQueryable<AppUser> q = Single ? db.Users.AsNoTracking()
            : db.Users.AsNoTracking().Where(u => db.UserApplications.Any(x => x.UserId == u.Id && x.ApplicationId == app.Id));
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
        var app = await flow.GetApplicationAsync(ct);
        var user = await MemberAsync(id, app, ct);
        return user is null ? null : new AdminUserView(await ToDtoAsync(user, app, ct), AdminSupport.ETagOf(user.ConcurrencyStamp));
    }

    public async Task<(OperationResult Result, AdminCreateUserResponse? Response)> CreateAsync(AdminCreateUserRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
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
                db.UserApplications.Add(new UserApplication { UserId = user.Id, ApplicationId = app.Id, JoinedAtUtc = clock.GetUtcNow(), Status = UserApplicationStatus.Active });
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
        var app = await flow.GetApplicationAsync(ct);
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
        var app = await flow.GetApplicationAsync(ct);
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
            await db.UserRoles.Where(ur => ur.UserId == id && db.Roles.Any(r => r.Id == ur.RoleId && r.ApplicationId == app.Id)).ExecuteDeleteAsync(ct);
            await db.UserApplications.Where(x => x.UserId == id && x.ApplicationId == app.Id).ExecuteDeleteAsync(ct);
        }
        await audit.WriteAsync(new AuditEntry("user.removed", app.Id, TargetType: "user", TargetId: id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> LockAsync(Guid id, DateTimeOffset? untilUtc, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
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
        var app = await flow.GetApplicationAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var result = await users.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await users.ResetAccessFailedCountAsync(user);
        await versions.BumpUserAsync(user, ct);
        await audit.WriteAsync(new AuditEntry("user.unlocked", app.Id, TargetType: "user", TargetId: id.ToString()), ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ResetTwoFactorAsync(Guid id, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
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
        var app = await flow.GetApplicationAsync(ct);
        var user = await MemberAsync(id, app, ct);
        if (user is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), []);
        return (OperationResult.Ok, [.. (await sessions.ListAsync(user, app, ct)).Select(s => new SessionDto(s.Id, s.CreatedAtUtc, s.Scopes))]);
    }

    public async Task<OperationResult> RevokeSessionsAsync(Guid id, string? sessionId, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
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

    private async Task<AppUser?> MemberAsync(Guid id, Auth2Application app, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString("D"));
        if (user is null) return null;
        return Single || await lookup.HasRowAsync(user, app, ct) ? user : null;
    }

    private async Task<List<UserDto>> ToDtosAsync(IEnumerable<AppUser> list, Auth2Application app, CancellationToken ct)
    {
        var result = new List<UserDto>();
        foreach (var u in list) result.Add(await ToDtoAsync(u, app, ct));
        return result;
    }

    private async Task<UserDto> ToDtoAsync(AppUser u, Auth2Application app, CancellationToken ct)
    {
        DateTimeOffset? lastLogin = Single ? null : await db.UserApplications.AsNoTracking()
            .Where(x => x.UserId == u.Id && x.ApplicationId == app.Id).Select(x => x.LastLoginUtc).FirstOrDefaultAsync(ct);
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

internal sealed class AdminApplicationService(
    Auth2DbContext db, IGlobalScope globalScope, IApplicationService applications, IClientProvisioner provisioner,
    IApplicationSettingsProvider settingsProvider, IAuditService audit, IOptions<Auth2ServerOptions> options) : IAdminApplicationService
{
    private bool Single => options.Value.Mode == ServerMode.SingleApplication;

    public async Task<PagedResult<ApplicationDto>> ListAsync(PageQuery query, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        IQueryable<Auth2Application> q = db.Applications.AsNoTracking();
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
        return new PagedResult<ApplicationDto>([.. items.Select(ToDto)], query.SafePage, query.SafePageSize, total);
    }

    public async Task<ApplicationDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return app is null ? null : ToDto(app);
    }

    public async Task<(OperationResult Result, CreateApplicationResponse? Response)> CreateAsync(CreateApplicationRequest request, CancellationToken ct = default)
    {
        if (Single) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), null);
        if (!ProjectKeyRules.IsValidForNewApplication(request.ProjectKey ?? "") || string.IsNullOrWhiteSpace(request.Name))
            return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        string? clientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId.Trim();
        using (globalScope.Begin())
        {
            var clash = await db.Applications.AnyAsync(a => a.ProjectKey == request.ProjectKey || a.ClientId == (clientId ?? request.ProjectKey), ct);
            if (clash) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);
        }
        var created = await applications.CreateAsync(request.ProjectKey!, request.Name, request.Description, clientId, ct);
        return (OperationResult.Ok, new CreateApplicationResponse(ToDto(created.Application), created.ClientSecret));
    }

    public async Task<(OperationResult Result, ApplicationDto? Application)> UpdateAsync(Guid id, UpdateApplicationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        using var _ = globalScope.Begin();
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        if (app.IsGlobalProject && !request.IsEnabled) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), null);   // never lock out the admins
        app.Name = request.Name.Trim();
        app.Description = request.Description;
        app.IsEnabled = request.IsEnabled;
        await db.SaveChangesAsync(ct);
        await provisioner.EnsureClientAsync(app, ct);   // the display name follows
        await audit.WriteAsync(new AuditEntry("application.updated", app.Id, TargetType: "application", TargetId: app.Id.ToString()), ct);
        return (OperationResult.Ok, ToDto(app));
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (Single) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        using var _ = globalScope.Begin();
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (app.IsGlobalProject) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await provisioner.DeleteClientAsync(app, ct);
        db.Applications.Remove(app);   // roles, permissions, groups and memberships go with it (cascade)
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        settingsProvider.Invalidate(id);
        await audit.WriteAsync(new AuditEntry("application.deleted", null, TargetType: "application", TargetId: id.ToString(),
            Data: new Dictionary<string, object?> { ["projectKey"] = app.ProjectKey }), ct);
        return OperationResult.Ok;
    }

    public async Task<(ApplicationSettings Settings, int Version)?> GetSettingsAsync(Guid id, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return app is null ? null : (app.Settings, app.SettingsVersion);
    }

    public async Task<(OperationResult Result, int Version)> UpdateSettingsAsync(Guid id, ApplicationSettings settings, bool isGlobalAdmin, int? ifMatchVersion, CancellationToken ct = default)
    {
        ApplicationSettings current; int version;
        using (globalScope.Begin())
        {
            var app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (app is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), 0);
            (current, version) = (app.Settings, app.SettingsVersion);
        }
        if (ifMatchVersion is { } v && v != version) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), version);

        var errors = ApplicationSettingsValidator.Validate(settings);
        if (errors.Count > 0) return (AdminSupport.Invalid(errors), version);
        if (!isGlobalAdmin)
        {
            var forbidden = SettingsEditability.ChangedGlobalOnly(current, settings);
            if (forbidden.Count > 0)
                return (AdminSupport.Invalid(forbidden.Select(p => new SettingsError(p, "Only a global admin may change this setting."))) with { ErrorCode = Auth2ErrorCodes.Forbidden }, version);
        }
        await applications.UpdateSettingsAsync(id, settings, ct);
        return (OperationResult.Ok, version + 1);
    }

    public async Task<(OperationResult Result, string? Secret)> RotateSecretAsync(Guid id, bool isGlobalAdmin, CancellationToken ct = default)
    {
        Auth2Application? app;
        using (globalScope.Begin()) app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        if (!isGlobalAdmin && !app.Settings.Clients.AllowProjectAdminRotateSecret) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), null);
        return (OperationResult.Ok, await applications.RotateClientSecretAsync(app.ProjectKey, ct));
    }

    private ApplicationDto ToDto(Auth2Application a) =>
        new(a.Id, Single ? null : a.ProjectKey, a.Name, a.Description, a.ClientId, a.IsEnabled, a.SettingsVersion, a.CreatedAtUtc);
}

internal sealed class AuditReader(Auth2DbContext db, IAuthFlowResolver flow, IGlobalScope globalScope) : IAuditReader
{
    public async Task<PagedResult<AuditEventDto>> QueryAsync(PageQuery query, string? action, Guid? actorUserId, string? targetType,
        DateTimeOffset? from, DateTimeOffset? to, bool allApplications, CancellationToken ct = default)
    {
        await flow.GetApplicationAsync(ct);   // selects the application for the query filter
        IDisposable? all = allApplications ? globalScope.Begin() : null;
        try
        {
            IQueryable<AuditEvent> q = db.AuditEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(action)) q = q.Where(e => e.Action == action);
            if (actorUserId is { } actor) q = q.Where(e => e.ActorUserId == actor);
            if (!string.IsNullOrWhiteSpace(targetType)) q = q.Where(e => e.TargetType == targetType);
            if (from is { } f) q = q.Where(e => e.OccurredAtUtc >= f);
            if (to is { } t) q = q.Where(e => e.OccurredAtUtc <= t);
            if (query.SafeSearch is { } s) q = q.Where(e => e.TargetId == s || e.Action.Contains(s));
            q = query.Descending && query.Sort is "time" ? q.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id) : q.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id);
            var total = await q.CountAsync(ct);
            var items = await q.Skip(query.Skip).Take(query.SafePageSize).ToListAsync(ct);
            return new PagedResult<AuditEventDto>([.. items.Select(e => new AuditEventDto(e.Id, e.OccurredAtUtc, e.ApplicationId, e.ActorUserId, e.ActorClientId,
                e.Action, e.TargetType, e.TargetId, e.IpAddress, e.UserAgent, e.DataJson))], query.SafePage, query.SafePageSize, total);
        }
        finally { all?.Dispose(); }
    }
}
