using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Providers;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public string Name { get; set; } = "";
    public ExternalProviderAdminDto? Provider { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var externalSvc = Svc<IAdminExternalService>();
        var list = await externalSvc.ListProvidersAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        Provider = list?.FirstOrDefault(p => string.Equals(p.Name, Name, StringComparison.OrdinalIgnoreCase));

        if (Provider is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var externalSvc = Svc<IAdminExternalService>();
        var (result, test) = await externalSvc.TestProviderAsync(CurrentProject!.Id, Name, HttpContext.RequestAborted);

        if (test?.Ok == true) TempData["Success"] = "Connection test succeeded.";
        else TempData["Error"] = $"Test failed: {test?.Reason ?? result.ErrorCode}";

        return RedirectToPage(new { projectKey = ProjectKey, name = Name });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var externalSvc = Svc<IAdminExternalService>();
        var result = await externalSvc.DeleteProviderAsync(CurrentProject!.Id, Name, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not delete provider.";
            return RedirectToPage(new { projectKey = ProjectKey, name = Name });
        }

        TempData["Success"] = $"Provider '{Name}' deleted.";
        return Redirect($"/admin/p/{ProjectKey}/providers");
    }
}
