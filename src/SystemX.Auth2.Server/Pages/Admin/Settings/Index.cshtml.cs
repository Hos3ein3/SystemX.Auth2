using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Pages.Admin.Settings;

public sealed class IndexModel : AdminPageModel
{
    public ProjectSettings CurrentSettings { get; private set; } = new();
    public int SettingsVersion { get; private set; }

    [BindProperty] public ProjectSettings InputSettings { get; set; } = new();
    [BindProperty] public int FormVersion { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var projectSvc = Svc<IAdminProjectService>();
        var settingsTuple = await projectSvc.GetSettingsAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        if (settingsTuple is not null)
        {
            CurrentSettings = settingsTuple.Value.Settings;
            SettingsVersion = settingsTuple.Value.Version;
            InputSettings = CurrentSettings;
            FormVersion = SettingsVersion;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var projectSvc = Svc<IAdminProjectService>();
        var (result, newVersion) = await projectSvc.UpdateSettingsAsync(
            CurrentProject!.Id,
            InputSettings,
            IsGlobalAdmin,
            FormVersion,
            HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Failed to save project settings.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        TempData["Success"] = "Project settings saved successfully.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
