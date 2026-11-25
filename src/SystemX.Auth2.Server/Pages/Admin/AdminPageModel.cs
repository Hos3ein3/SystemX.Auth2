using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Pages.Admin;

/// <summary>
/// Base class for all admin console pages. Ensures caller is authorized for the project in the route.
/// </summary>
public abstract class AdminPageModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string ProjectKey { get; set; } = "_global";

    public Project? CurrentProject { get; protected set; }
    public bool IsGlobalAdmin => User.IsInRole("GlobalAdmin") || User.IsInRole("Developer") || User.HasClaim("scope", "auth2.admin.global") || User.HasClaim("role", "GlobalAdmin") || User.HasClaim("role", "Developer");

    protected T Svc<T>() where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    /// <summary>Ensures the caller has permission for the route project. Returns 404 for wrong project, not 403.</summary>
    protected async Task<bool> AuthorizeAdminAsync(CancellationToken ct = default)
    {
        var routeKey = RouteData.Values["projectKey"]?.ToString() ?? ProjectKey;
        if (string.IsNullOrWhiteSpace(routeKey)) routeKey = "_global";
        ProjectKey = routeKey.ToLowerInvariant();

        var projectService = Svc<IProjectService>();
        CurrentProject = await projectService.FindByProjectKeyAsync(ProjectKey, ct);
        if (CurrentProject is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return false;
        }

        // ProjectAdmin check: If not GlobalAdmin, must match caller's ProjectId
        if (!IsGlobalAdmin)
        {
            var userProjectId = User.FindFirst(Auth2Claims.ProjectId)?.Value;
            if (!string.IsNullOrEmpty(userProjectId) && CurrentProject.Id != Guid.Empty && userProjectId != CurrentProject.Id.ToString("D"))
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return false;
            }

            if (!User.IsInRole("ProjectAdmin") && !User.IsInRole("Developer") && !User.IsInRole("GlobalAdmin") && !User.IsInRole("Admin") &&
                !User.HasClaim("role", "ProjectAdmin") && !User.HasClaim("role", "Admin") && !User.HasClaim("role", "GlobalAdmin"))
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return false;
            }
        }

        // The services of this request now work on the project of the route (like the admin API does).
        Svc<IProjectSelection>().ProjectKey = ProjectKey;
        Svc<IProjectScope>().Set(CurrentProject.Id, CurrentProject.ProjectKey);
        await Svc<IProjectContext>().GetRequiredAsync(ct);
        return true;
    }

    protected void SetSuccess(string message) => TempData["Success"] = message;
    protected void SetError(string message) => TempData["Error"] = message;
}
