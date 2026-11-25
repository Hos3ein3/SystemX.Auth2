using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Audit;

public sealed class IndexModel : AdminPageModel
{
    public record ApplicationLookupInfo(Guid Id, string Name, string? ClientId, string ProjectKey);

    public PagedResult<AuditEventDto> Events { get; private set; } = new([], 1, 25, 0);
    public Dictionary<Guid, string> Usernames { get; private set; } = [];
    public Dictionary<string, ApplicationLookupInfo> ApplicationLookups { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string? ActionFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? TargetType { get; set; }
    [BindProperty(SupportsGet = true)] public DateTimeOffset? FromDate { get; set; }
    [BindProperty(SupportsGet = true)] public DateTimeOffset? ToDate { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var auditReader = Svc<IAuditReader>();
        bool allProjects = IsGlobalAdmin && ProjectKey == "_global";

        Events = await auditReader.QueryAsync(
            new PageQuery(PageNumber, PageSize, Q),
            ActionFilter,
            null,
            TargetType,
            FromDate,
            ToDate,
            allProjects,
            HttpContext.RequestAborted);

        var db = Svc<Auth2DbContext>();

        var userIds = Events.Items
            .Select(e => e.ActorUserId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Union(Events.Items
                .Where(e => (string.Equals(e.TargetType, "user", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(e.TargetType, "AppUser", StringComparison.OrdinalIgnoreCase)) &&
                            Guid.TryParse(e.TargetId, out _))
                .Select(e => Guid.Parse(e.TargetId!)))
            .Distinct()
            .ToList();

        if (userIds.Count > 0)
        {
            Usernames = await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? u.Id.ToString(), HttpContext.RequestAborted);
        }

        // Resolve applications for clickable links and human-readable names
        var apps = await (from a in db.Set<Auth2Application>().IgnoreQueryFilters().AsNoTracking()
                          join p in db.Projects.IgnoreQueryFilters().AsNoTracking() on a.ProjectId equals p.Id
                          select new ApplicationLookupInfo(a.Id, a.Name, a.ClientId, p.ProjectKey))
                         .ToListAsync(HttpContext.RequestAborted);

        ApplicationLookups = new Dictionary<string, ApplicationLookupInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            ApplicationLookups[app.Id.ToString()] = app;
            ApplicationLookups[app.Id.ToString("D")] = app;
            if (!string.IsNullOrEmpty(app.ClientId))
            {
                ApplicationLookups[app.ClientId] = app;
            }
        }

        return Page();
    }
}
