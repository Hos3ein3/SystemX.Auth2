using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages;

/// <summary>Project item for the developer dashboard.</summary>
public sealed class ProjectOverviewItem
{
    public Guid Id { get; set; }
    public string ProjectKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int ApplicationsCount { get; set; }
    public int UsersCount { get; set; }
}

/// <summary>Developer metrics aggregated across projects and audit events.</summary>
public sealed class DeveloperMetrics
{
    public int TotalProjects { get; set; }
    public int ActiveProjects { get; set; }
    public int TotalApplications { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveSessions { get; set; }
    public int LoginsSuccess24h { get; set; }
    public int LoginsFailed24h { get; set; }
    public int Lockouts24h { get; set; }
    public int TotalLogins24h => LoginsSuccess24h + LoginsFailed24h;
    public double SuccessRate => TotalLogins24h > 0 ? (double)LoginsSuccess24h / TotalLogins24h * 100.0 : 100.0;
}

/// <summary>
/// Root Developer Dashboard for SystemX.Auth2 Server.
/// Shows project activity, fail audits, system logs, health metrics, and visual status bars.
/// </summary>
public sealed class IndexModel(Auth2DbContext db, IAdminJobService jobService, TimeProvider clock, IGlobalScope globalScope) : PageModel
{
    public DeveloperMetrics Metrics { get; private set; } = new();
    public List<ProjectOverviewItem> Projects { get; private set; } = [];
    public List<AuditEvent> FailedAudits { get; private set; } = [];
    public List<AuditEvent> RecentAudits { get; private set; } = [];
    public IReadOnlyList<JobStatusDto> JobStatuses { get; private set; } = [];
    public bool IsGlobalAdmin { get; private set; }
    public string ProjectKey => "_global";

    public async Task<IActionResult> OnGetAsync([FromQuery] string? returnUrl = null, [FromQuery] string? client_id = null)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            var targetClientId = !string.IsNullOrEmpty(client_id) ? client_id : "_global";
            var loginUrl = $"/Account/Login?client_id={Uri.EscapeDataString(targetClientId)}";
            if (!string.IsNullOrEmpty(returnUrl))
            {
                loginUrl += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
            }
            return Redirect(loginUrl);
        }

        IsGlobalAdmin = User.IsInRole(Auth2Roles.GlobalAdmin)
                     || User.IsInRole(Auth2Roles.Developer)
                     || User.HasClaim("scope", "auth2.admin.global");

        // The roles are already claims of the session (SessionRoleClaims reads them for the project of the cookie). A second lookup here would
        // need a selected project, and this page has none.
        if (!IsGlobalAdmin)
        {
            var userProjectKey = User.FindFirst(Auth2Claims.ProjectKey)?.Value;
            if (!string.IsNullOrEmpty(userProjectKey))
            {
                return Redirect($"/admin/p/{userProjectKey}");
            }
            return Redirect("/Manage/Profile");
        }

        using var _ = globalScope.Begin();

        // Metrics calculations
        var since24h = clock.GetUtcNow().AddHours(-24);

        var totalProjects = await db.Projects.CountAsync();
        var activeProjects = await db.Projects.CountAsync(p => p.IsEnabled);
        var totalApps = await db.Set<Auth2Application>().CountAsync();
        var totalUsers = await db.Users.CountAsync();
        var activeSessions = await db.UserTokens.CountAsync(t => t.LoginProvider == "[Auth2]" && t.Name == "Session");

        var loginsSuccess24h = await db.AuditEvents.CountAsync(e => e.OccurredAtUtc >= since24h && e.Action == "login.success");
        var loginsFailed24h = await db.AuditEvents.CountAsync(e => e.OccurredAtUtc >= since24h && (e.Action == "login.failed" || e.Action.Contains("fail")));
        var lockouts24h = await db.AuditEvents.CountAsync(e => e.OccurredAtUtc >= since24h && e.Action.Contains("lockout"));

        Metrics = new DeveloperMetrics
        {
            TotalProjects = totalProjects,
            ActiveProjects = activeProjects,
            TotalApplications = totalApps,
            TotalUsers = totalUsers,
            ActiveSessions = activeSessions,
            LoginsSuccess24h = loginsSuccess24h,
            LoginsFailed24h = loginsFailed24h,
            Lockouts24h = lockouts24h
        };

        // Projects with counts
        var rawProjects = await db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        var appCounts = await db.Set<Auth2Application>().AsNoTracking()
            .GroupBy(a => a.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count);
        var userCounts = await db.UserProjects.AsNoTracking()
            .GroupBy(u => u.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count);

        Projects = rawProjects.Select(p => new ProjectOverviewItem
        {
            Id = p.Id,
            ProjectKey = p.ProjectKey,
            Name = p.Name,
            Description = p.Description,
            IsEnabled = p.IsEnabled,
            CreatedAtUtc = p.CreatedAtUtc,
            ApplicationsCount = appCounts.GetValueOrDefault(p.Id, 0),
            UsersCount = userCounts.GetValueOrDefault(p.Id, 0),
        }).ToList();

        // Failed audit events
        FailedAudits = await db.AuditEvents.AsNoTracking()
            .Where(e => e.Action.Contains("failed") || e.Action.Contains("lockout") || e.Action.Contains("denied") || e.Action.Contains("forbidden") || e.Action.Contains("error"))
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(15)
            .ToListAsync();

        // Recent general audits
        RecentAudits = await db.AuditEvents.AsNoTracking()
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(25)
            .ToListAsync();

        try
        {
            JobStatuses = await jobService.GetStatusAsync(HttpContext.RequestAborted);
        }
        catch
        {
            JobStatuses = [];
        }

        return Page();
    }
}
