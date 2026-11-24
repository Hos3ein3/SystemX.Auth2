using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Providers;

public sealed class IndexModel : AdminPageModel
{
    public IReadOnlyList<ExternalProviderAdminDto> Providers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var externalSvc = Svc<IAdminExternalService>();
        var list = await externalSvc.ListProvidersAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        Providers = list ?? [];
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(string name)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var externalSvc = Svc<IAdminExternalService>();
        var (result, test) = await externalSvc.TestProviderAsync(CurrentProject!.Id, name, HttpContext.RequestAborted);
        if (test?.Ok == true)
        {
            TempData["Success"] = $"Provider '{name}' tested successfully. Connection verified.";
        }
        else
        {
            TempData["Error"] = $"Provider '{name}' test failed: {test?.Reason ?? result.ErrorCode}";
        }
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string name)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        var externalSvc = Svc<IAdminExternalService>();
        var result = await externalSvc.DeleteProviderAsync(CurrentProject!.Id, name, HttpContext.RequestAborted);
        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to delete provider.";
        else TempData["Success"] = $"Provider '{name}' deleted.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
