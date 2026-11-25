using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Applications;

public sealed class IndexModel : AdminPageModel
{
    public IReadOnlyList<ApplicationDto> Applications { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var appSvc = Svc<IAdminApplicationService>();
        var all = await appSvc.ListAsync(CurrentProject!.Id, HttpContext.RequestAborted);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var filter = Q.Trim().ToLowerInvariant();
            all = all.Where(a => a.Name.ToLowerInvariant().Contains(filter) || a.ClientId.ToLowerInvariant().Contains(filter)).ToList();
        }

        Applications = all;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var appSvc = Svc<IAdminApplicationService>();
        var result = await appSvc.DeleteAsync(CurrentProject!.Id, id, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to delete application.";
        else TempData["Success"] = "Application deleted.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostRotateSecretAsync(Guid id)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var appSvc = Svc<IAdminApplicationService>();
        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        var (result, setup) = await appSvc.RotateSecretAsync(CurrentProject!.Id, id, IsGlobalAdmin, serverUrl, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Failed to revoke and rotate application secret key.";
        }
        else
        {
            TempData["Success"] = $"Secret key revoked and rotated successfully. New Client Secret: {setup?.ClientSecret} (Please copy and store it now; it will not be displayed again).";
        }
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
