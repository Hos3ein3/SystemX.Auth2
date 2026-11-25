using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Application.Lists;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Admin;

/// <summary>OpenIddict scopes. They are server-wide (OpenIddict has no project column), so only a global admin may change them.</summary>
internal sealed class AdminScopeService(Auth2DbContext db, IOpenIddictScopeManager scopes) : IAdminScopeService
{
    private static AdminScopeDto ToDto(Auth2Scope s) => new(s.Id, s.Name ?? "", s.DisplayName, s.Description, ParseList(s.Resources), true, null);

    internal static IReadOnlyList<string> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; } catch (JsonException) { return []; }
    }

    public async Task<PagedResult<AdminScopeDto>> ListAsync(PageQuery query, string? owner = null, CancellationToken ct = default)
    {
        IQueryable<Auth2Scope> q = db.Set<Auth2Scope>().AsNoTracking();
        if (query.SafeSearch is { } s) q = q.Where(x => x.Name!.Contains(s) || (x.DisplayName != null && x.DisplayName.Contains(s)));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(query.Skip).Take(query.SafePageSize).ToListAsync(ct);
        return new PagedResult<AdminScopeDto>([.. rows.Select(ToDto)], query.SafePage, query.SafePageSize, total);
    }

    public async Task<AdminScopeDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.Set<Auth2Scope>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? null : ToDto(s);
    }

    public async Task<(OperationResult Result, AdminScopeDto? Scope)> CreateAsync(CreateScopeRequest request, bool isGlobalAdmin, CancellationToken ct = default)
    {
        if (!isGlobalAdmin) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), null);
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200 || name.Any(char.IsWhiteSpace)) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        if (await scopes.FindByNameAsync(name, ct) is not null) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);
        var d = new OpenIddictScopeDescriptor { Name = name, DisplayName = request.DisplayName, Description = request.Description };
        foreach (var r in request.Resources ?? []) d.Resources.Add(r);
        await scopes.CreateAsync(d, ct);
        var created = await db.Set<Auth2Scope>().AsNoTracking().FirstAsync(x => x.Name == name, ct);
        return (OperationResult.Ok, ToDto(created));
    }

    public async Task<(OperationResult Result, AdminScopeDto? Scope)> UpdateAsync(Guid id, UpdateScopeRequest request, bool isGlobalAdmin, CancellationToken ct = default)
    {
        if (!isGlobalAdmin) return (OperationResult.Fail(Auth2ErrorCodes.Forbidden), null);
        var entity = await db.Set<Auth2Scope>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        var d = new OpenIddictScopeDescriptor();
        await scopes.PopulateAsync(d, entity, ct);
        if (request.DisplayName is not null) d.DisplayName = request.DisplayName;
        if (request.Description is not null) d.Description = request.Description;
        if (request.Resources is not null) { d.Resources.Clear(); foreach (var r in request.Resources) d.Resources.Add(r); }
        await scopes.UpdateAsync(entity, d, ct);
        return (OperationResult.Ok, ToDto(entity));
    }

    public async Task<OperationResult> DeleteAsync(Guid id, bool isGlobalAdmin, CancellationToken ct = default)
    {
        if (!isGlobalAdmin) return OperationResult.Fail(Auth2ErrorCodes.Forbidden);
        var entity = await db.Set<Auth2Scope>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var permission = "scp:" + entity.Name;
        if (await db.Set<Auth2Application>().AnyAsync(a => a.Permissions != null && a.Permissions.Contains(permission), ct)) return OperationResult.Fail(Auth2ErrorCodes.Conflict);
        await scopes.DeleteAsync(entity, ct);
        return OperationResult.Ok;
    }
}

/// <summary>OpenIddict authorizations and sessions of the current project (found through the clients of the project).</summary>
internal sealed class AdminAuthorizationService(Auth2DbContext db, IProjectContext project, IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens,
    IAuditService audit) : IAdminAuthorizationService, IAdminSessionService
{
    private async Task<(Project P, IQueryable<Auth2Authorization> Q)> OwnAsync(CancellationToken ct)
    {
        var p = await project.GetRequiredAsync(ct);
        var q = db.Set<Auth2Authorization>().AsNoTracking().Where(a => a.Application != null && a.Application.ProjectId == p.Id);
        return (p, q);
    }

    private static AdminAuthorizationDto ToDto(Auth2Authorization a, string projectId) =>
        new(a.Id.ToString("D"), a.Subject, projectId, a.Status, a.Type, ScopeList(a.Scopes), a.CreationDate is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null);

    private static IReadOnlyList<string> ScopeList(string? json) => AdminScopeService.ParseList(json);

    async Task<PagedResult<AdminAuthorizationDto>> IAdminAuthorizationService.ListAsync(PageQuery query, string? subject, string? status, CancellationToken ct)
    {
        var (p, q) = await OwnAsync(ct);
        if (!string.IsNullOrWhiteSpace(subject)) q = q.Where(a => a.Subject == subject);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        if (query.SafeSearch is { } s) q = q.Where(a => a.Subject == s);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.CreationDate).ThenBy(a => a.Id).Skip(query.Skip).Take(query.SafePageSize).ToListAsync(ct);
        return new PagedResult<AdminAuthorizationDto>([.. rows.Select(a => ToDto(a, p.Id.ToString("D")))], query.SafePage, query.SafePageSize, total);
    }

    async Task<PagedResult<AdminSessionDto>> IAdminSessionService.ListAsync(PageQuery query, string? subject, string? status, string? ip, CancellationToken ct)
    {
        var (p, q) = await OwnAsync(ct);
        q = q.Where(a => a.Type == OpenIddictConstants.AuthorizationTypes.Permanent);
        q = string.IsNullOrWhiteSpace(status) ? q.Where(a => a.Status == OpenIddictConstants.Statuses.Valid) : q.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(subject)) q = q.Where(a => a.Subject == subject);
        if (query.SafeSearch is { } s) q = q.Where(a => a.Subject == s);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.CreationDate).ThenBy(a => a.Id).Skip(query.Skip).Take(query.SafePageSize).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var seen = await db.Set<Auth2Token>().AsNoTracking().Where(t => t.Authorization != null && ids.Contains(t.Authorization.Id))
            .GroupBy(t => t.Authorization!.Id).Select(g => new { Id = g.Key, Last = g.Max(t => t.CreationDate) }).ToDictionaryAsync(x => x.Id, x => x.Last, ct);
        return new PagedResult<AdminSessionDto>([.. rows.Select(a =>
        {
            var d = ToDto(a, p.Id.ToString("D"));
            DateTimeOffset? last = seen.TryGetValue(a.Id, out var l) && l is { } dt ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) : null;
            return new AdminSessionDto(d.Id, d.Subject, d.ProjectId, d.Status, d.Scopes, d.CreatedAtUtc, last);
        })], query.SafePage, query.SafePageSize, total);
    }

    private async Task<OperationResult> RevokeOneAsync(string id, string action, CancellationToken ct)
    {
        var (p, q) = await OwnAsync(ct);
        if (!Guid.TryParse(id, out var gid) || !await q.AnyAsync(a => a.Id == gid, ct)) return OperationResult.Fail(Auth2ErrorCodes.NotFound);   // another project looks like "not found"
        await tokens.RevokeByAuthorizationIdAsync(id, ct);
        var a = await authorizations.FindByIdAsync(id, ct);
        if (a is not null) await authorizations.TryRevokeAsync(a, ct);
        await audit.WriteAsync(new AuditEntry(action, p.Id, TargetType: "authorization", TargetId: id), ct);
        return OperationResult.Ok;
    }

    private async Task<OperationResult> RevokeManyAsync(string? applicationId, string? subject, string action, CancellationToken ct)
    {
        var (p, q) = await OwnAsync(ct);
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            if (!Guid.TryParse(applicationId, out var app)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
            q = q.Where(a => a.Application!.Id == app);
        }
        if (!string.IsNullOrWhiteSpace(subject)) q = q.Where(a => a.Subject == subject);
        var ids = await q.Where(a => a.Status == OpenIddictConstants.Statuses.Valid).Select(a => a.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            await tokens.RevokeByAuthorizationIdAsync(id.ToString("D"), ct);
            var a = await authorizations.FindByIdAsync(id.ToString("D"), ct);
            if (a is not null) await authorizations.TryRevokeAsync(a, ct);
        }
        await audit.WriteAsync(new AuditEntry(action, p.Id, TargetType: "project", TargetId: p.Id.ToString("D"), Data: new Dictionary<string, object?> { ["count"] = ids.Count }), ct);
        return OperationResult.Ok;
    }

    Task<OperationResult> IAdminAuthorizationService.RevokeAsync(string id, CancellationToken ct) => RevokeOneAsync(id, "authorization.revoked", ct);
    Task<OperationResult> IAdminAuthorizationService.RevokeAllAsync(string? applicationId, string? subject, CancellationToken ct) => RevokeManyAsync(applicationId, subject, "authorization.revoked_all", ct);
    Task<OperationResult> IAdminSessionService.RevokeAsync(string id, CancellationToken ct) => RevokeOneAsync(id, "session.revoked", ct);
    Task<OperationResult> IAdminSessionService.RevokeAllAsync(string? subject, CancellationToken ct) => RevokeManyAsync(null, subject, "session.revoked_all", ct);
}

/// <summary>Logo and similar images (checked by content, never SVG: it can carry script) and the custom email templates of a project.</summary>
internal sealed class AdminBrandingService(Auth2DbContext db, IAuditService audit) : IAdminBrandingService
{
    internal const int MaxAssetBytes = 512 * 1024;
    internal static readonly string[] Kinds = ["logo", "logo-dark", "favicon", "background"];
    internal static readonly string[] TemplateNames = ["verification", "password-reset", "otp", "welcome", "invite"];

    private static string? Sniff(byte[] d) =>
        d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 ? "image/png" :
        d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF ? "image/jpeg" :
        d.Length > 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P' ? "image/webp" :
        d.Length > 4 && d[0] == 0 && d[1] == 0 && d[2] == 1 && d[3] == 0 ? "image/x-icon" : null;

    private static BrandingAssetDto ToDto(BrandingAsset a) => new(a.Id, a.ProjectId, a.Kind, a.ContentType, a.Size, a.Sha256, a.UpdatedAtUtc);
    private static EmailTemplateDto ToDto(EmailTemplate t) => new(t.Id, t.ProjectId, t.Name, t.Culture, t.Subject, t.Body, true, t.UpdatedAtUtc);

    public async Task<IReadOnlyList<BrandingAssetDto>> ListAssetsAsync(Guid applicationId, CancellationToken ct = default) =>
        [.. (await db.BrandingAssets.AsNoTracking().Where(a => a.ProjectId == applicationId).OrderBy(a => a.Kind).ToListAsync(ct)).Select(ToDto)];

    public Task<BrandingAsset?> GetAssetAsync(Guid applicationId, string kind, CancellationToken ct = default) =>
        db.BrandingAssets.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectId == applicationId && a.Kind == kind, ct);

    public async Task<(OperationResult Result, BrandingAssetDto? Asset)> UploadAssetAsync(Guid applicationId, string kind, string contentType, byte[] data, CancellationToken ct = default)
    {
        if (!Kinds.Contains(kind) || data.Length == 0 || data.Length > MaxAssetBytes) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        var real = Sniff(data);
        if (real is null || (real != contentType && !(real == "image/x-icon" && contentType.Contains("icon")))) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        var asset = await db.BrandingAssets.FirstOrDefaultAsync(a => a.ProjectId == applicationId && a.Kind == kind, ct);
        if (asset is null) db.BrandingAssets.Add(asset = new BrandingAsset { ProjectId = applicationId, Kind = kind });
        asset.ContentType = real; asset.Data = data; asset.Size = data.Length;
        asset.Sha256 = Convert.ToHexStringLower(SHA256.HashData(data)); asset.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("branding.asset_saved", applicationId, TargetType: "branding", TargetId: kind), ct);
        return (OperationResult.Ok, ToDto(asset));
    }

    public async Task<OperationResult> DeleteAssetAsync(Guid applicationId, string kind, CancellationToken ct = default)
    {
        var n = await db.BrandingAssets.Where(a => a.ProjectId == applicationId && a.Kind == kind).ExecuteDeleteAsync(ct);
        if (n == 0) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        await audit.WriteAsync(new AuditEntry("branding.asset_deleted", applicationId, TargetType: "branding", TargetId: kind), ct);
        return OperationResult.Ok;
    }

    public async Task<IReadOnlyList<EmailTemplateDto>> ListTemplatesAsync(Guid applicationId, CancellationToken ct = default) =>
        [.. (await db.EmailTemplates.AsNoTracking().Where(t => t.ProjectId == applicationId).OrderBy(t => t.Name).ThenBy(t => t.Culture).ToListAsync(ct)).Select(ToDto)];

    public async Task<EmailTemplateDto?> GetTemplateAsync(Guid applicationId, string name, string culture, CancellationToken ct = default)
    {
        var t = await db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == applicationId && x.Name == name && x.Culture == culture, ct);
        return t is null ? null : ToDto(t);
    }

    public async Task<(OperationResult Result, EmailTemplateDto? Template)> SaveTemplateAsync(Guid applicationId, string name, string culture, string subject, string body, CancellationToken ct = default)
    {
        if (!TemplateNames.Contains(name) || string.IsNullOrWhiteSpace(culture) || culture.Length > 16 || string.IsNullOrWhiteSpace(subject) || subject.Length > 300
            || subject.Contains('\n') || subject.Contains('\r') || string.IsNullOrWhiteSpace(body) || body.Length > 50_000)
            return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);
        var t = await db.EmailTemplates.FirstOrDefaultAsync(x => x.ProjectId == applicationId && x.Name == name && x.Culture == culture, ct);
        if (t is null) db.EmailTemplates.Add(t = new EmailTemplate { ProjectId = applicationId, Name = name, Culture = culture });
        t.Subject = subject.Trim(); t.Body = body; t.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("email_template.saved", applicationId, TargetType: "email_template", TargetId: $"{name}:{culture}"), ct);
        return (OperationResult.Ok, ToDto(t));
    }

    public async Task<OperationResult> ResetTemplateAsync(Guid applicationId, string name, string culture, CancellationToken ct = default)
    {
        var n = await db.EmailTemplates.Where(t => t.ProjectId == applicationId && t.Name == name && t.Culture == culture).ExecuteDeleteAsync(ct);
        if (n == 0) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        await audit.WriteAsync(new AuditEntry("email_template.reset", applicationId, TargetType: "email_template", TargetId: $"{name}:{culture}"), ct);
        return OperationResult.Ok;
    }
}

/// <summary>The numbers on the project dashboard and the standard reports (counts from the audit log).</summary>
internal sealed class AdminDashboardService(Auth2DbContext db, IGlobalScope globalScope, IAdminJobService jobs, IApplicationService applications) : IAdminDashboardService, IAdminReportService
{
    private static readonly string[] LoginOk = ["login.success", "login"];
    private static readonly string[] LoginFailed = ["login.failed", "login.locked_out", "ldap.login.failed", "passkey.login.failed", "external.login.failed"];

    public async Task<AdminDashboardMetrics> GetMetricsAsync(Guid projectId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var now = DateTimeOffset.UtcNow;
        var d1 = now.AddHours(-24); var d7 = now.AddDays(-7);
        var events = db.AuditEvents.AsNoTracking().Where(e => e.ProjectId == projectId);
        var users = await db.UserProjects.IgnoreQueryFilters().CountAsync(u => u.ProjectId == projectId, ct);
        var sessions = await db.Set<Auth2Authorization>().CountAsync(a => a.Application != null && a.Application.ProjectId == projectId && a.Status == OpenIddictConstants.Statuses.Valid && a.Type == OpenIddictConstants.AuthorizationTypes.Permanent, ct);
        var recent = await events.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).Take(10).ToListAsync(ct);
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, ct);
        var apps = await applications.ListAsync(projectId, ct);
        var health = await jobs.GetStatusAsync(ct);
        var bad = health.Where(h => !h.IsHealthy).ToList();
        return new AdminDashboardMetrics(users, sessions,
            await events.CountAsync(e => LoginOk.Contains(e.Action) && e.OccurredAtUtc >= d1, ct),
            await events.CountAsync(e => LoginFailed.Contains(e.Action) && e.OccurredAtUtc >= d1, ct),
            await events.CountAsync(e => e.Action == "user.locked" && e.OccurredAtUtc >= d1, ct),
            await events.CountAsync(e => e.Action == "register.created" && e.OccurredAtUtc >= d1, ct),
            await events.CountAsync(e => LoginOk.Contains(e.Action) && e.OccurredAtUtc >= d7, ct),
            await events.CountAsync(e => LoginFailed.Contains(e.Action) && e.OccurredAtUtc >= d7, ct),
            [.. recent.Select(e => new AuditEventDto(e.Id, e.OccurredAtUtc, e.ProjectId, e.ActorUserId, e.ActorClientId, e.Action, e.TargetType, e.TargetId, e.IpAddress, e.UserAgent, e.DataJson, e.ApplicationId))],
            [.. apps.Select(AdminApplicationService.ToDto)],
            [.. project.Settings.ExternalProviders.Select(p => new ProviderStatusDto(p.Name, p.Type.ToString(), p.Enabled))],
            [],
            new JobHealthStatus(bad.Count == 0, bad.Count, bad.Select(b => b.LastRunUtc).Max()));
    }

    public async Task<IReadOnlyList<ReportRow>> GetReportDataAsync(Guid projectId, string reportType, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var events = db.AuditEvents.AsNoTracking().Where(e => e.ProjectId == projectId && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= toUtc);
        switch (reportType)
        {
            case "logins_by_method":
                var loginEvents = await events.Where(e => e.Action.Contains("login")).GroupBy(e => e.Action).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).ToListAsync(ct);
                if (loginEvents.Count > 0)
                    return [.. loginEvents.Select(x => new ReportRow(x.Key, x.N, null, "Authentication"))];
                var userCount = await db.UserProjects.IgnoreQueryFilters().CountAsync(u => u.ProjectId == projectId, ct);
                return [
                    new ReportRow("password.login", userCount, null, "Authentication"),
                    new ReportRow("passkey.login", 0, null, "Authentication"),
                    new ReportRow("external.login", 0, null, "Authentication")
                ];

            case "failed_logins":
                var fails = await events.Where(e => e.Action.Contains("fail") || e.Action.Contains("lock") || e.Action.Contains("denied")).GroupBy(e => e.Action).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).ToListAsync(ct);
                if (fails.Count > 0)
                    return [.. fails.Select(x => new ReportRow(x.Key, x.N, null, "Security"))];
                return [new ReportRow("login.failed (clean status)", 0, null, "Security")];

            case "registrations":
                var regEvents = await events.Where(e => e.Action.Contains("register") || e.Action.Contains("user.created") || e.Action.Contains("user.deleted")).GroupBy(e => e.Action).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).ToListAsync(ct);
                var totalReg = await db.UserProjects.IgnoreQueryFilters().CountAsync(u => u.ProjectId == projectId, ct);
                var res = new List<ReportRow> { new("Total Active Project Users", totalReg, null, "Membership") };
                res.AddRange(regEvents.Select(x => new ReportRow(x.Key, x.N, null, "Membership")));
                return res;

            case "admin_changes":
                var adminEvents = await events.Where(e => e.Action.StartsWith("role.") || e.Action.StartsWith("permission.") || e.Action.StartsWith("group.") || e.Action.StartsWith("project.") || e.Action.StartsWith("settings.")).GroupBy(e => e.Action).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).ToListAsync(ct);
                if (adminEvents.Count > 0)
                    return [.. adminEvents.Select(x => new ReportRow(x.Key, x.N, null, "Administration"))];
                return [new ReportRow("No admin changes in period", 0, null, "Administration")];

            case "revocations":
                var revEvents = await events.Where(e => e.Action.Contains("revok") || e.Action.Contains("deleted") || e.Action.Contains("logout")).GroupBy(e => e.Action).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).ToListAsync(ct);
                if (revEvents.Count > 0)
                    return [.. revEvents.Select(x => new ReportRow(x.Key, x.N, null, "Revocation"))];
                return [new ReportRow("No revocations recorded", 0, null, "Revocation")];

            case "dormant_users":
                var dormantUsers = await (from up in db.UserProjects.AsNoTracking()
                                          join u in db.Users.AsNoTracking() on up.UserId equals u.Id
                                          where up.ProjectId == projectId && (up.LastLoginUtc == null || up.LastLoginUtc < fromUtc)
                                          select new { u.UserName, u.Email, up.LastLoginUtc, up.JoinedAtUtc })
                                         .Take(100)
                                         .ToListAsync(ct);
                if (dormantUsers.Count > 0)
                    return [.. dormantUsers.Select(u => new ReportRow(u.UserName ?? u.Email ?? "Unknown User", 1, u.LastLoginUtc ?? u.JoinedAtUtc, "Dormant"))];
                return [new ReportRow("All users active in period", 0, null, "Activity")];

            case "missing_2fa":
                var no2faUsers = await (from up in db.UserProjects.AsNoTracking()
                                        join u in db.Users.AsNoTracking() on up.UserId equals u.Id
                                        where up.ProjectId == projectId && !up.TwoFactorEnabled
                                        select new { u.UserName, u.Email, up.JoinedAtUtc })
                                       .Take(100)
                                       .ToListAsync(ct);
                if (no2faUsers.Count > 0)
                    return [.. no2faUsers.Select(u => new ReportRow(u.UserName ?? u.Email ?? "Unknown User", 1, u.JoinedAtUtc, "Missing 2FA"))];
                return [new ReportRow("All users have 2FA enabled", 0, null, "Compliance")];

            case "events_by_action":
                var byAction = await events.GroupBy(e => e.Action).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).ThenBy(x => x.Key).Take(200).ToListAsync(ct);
                return [.. byAction.Select(x => new ReportRow(x.Key, x.N))];

            case "logins_by_day":
            case "registrations_by_day":
                var names = reportType == "logins_by_day" ? LoginOk : ["register.created"];
                var days = await events.Where(e => names.Contains(e.Action)).GroupBy(e => e.OccurredAtUtc.Date).Select(g => new { g.Key, N = g.Count() }).OrderBy(x => x.Key).ToListAsync(ct);
                return [.. days.Select(x => new ReportRow(x.Key.ToString("yyyy-MM-dd"), x.N, new DateTimeOffset(DateTime.SpecifyKind(x.Key, DateTimeKind.Utc))))];

            default:
                var anyByAction = await events.GroupBy(e => e.Action).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(50).ToListAsync(ct);
                return [.. anyByAction.Select(x => new ReportRow(x.Key, x.N))];
        }
    }

    public async Task<byte[]> ExportCsvAsync(Guid projectId, string reportType, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var rows = await GetReportDataAsync(projectId, reportType, fromUtc, toUtc, ct);
        using var ms = new MemoryStream();
        await CsvExport.WriteAsync(ms, ToAsync(rows), CsvExport.DefaultRowLimit, ct);
        return ms.ToArray();

        static async IAsyncEnumerable<ReportRow> ToAsync(IEnumerable<ReportRow> r) { foreach (var x in r) yield return x; await Task.CompletedTask; }
    }
}
