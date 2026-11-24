using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Settings;

public sealed class EmailTemplatesModel : AdminPageModel
{
    public IReadOnlyList<EmailTemplateDto> Templates { get; private set; } = [];

    [BindProperty] public string TemplateName { get; set; } = "confirm_email";
    [BindProperty] public string Culture { get; set; } = "en";
    [BindProperty] public string Subject { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var brandingSvc = Svc<IAdminBrandingService>();
        Templates = await brandingSvc.ListTemplatesAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var brandingSvc = Svc<IAdminBrandingService>();
        var (result, tpl) = await brandingSvc.SaveTemplateAsync(CurrentProject!.Id, TemplateName, Culture, Subject, Body, HttpContext.RequestAborted);

        if (!result.Succeeded) TempData["Error"] = "Failed to save email template.";
        else TempData["Success"] = $"Template '{TemplateName}' ({Culture}) saved.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostResetAsync(string name, string culture)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var brandingSvc = Svc<IAdminBrandingService>();
        var result = await brandingSvc.ResetTemplateAsync(CurrentProject!.Id, name, culture, HttpContext.RequestAborted);

        if (!result.Succeeded) TempData["Error"] = "Failed to reset email template.";
        else TempData["Success"] = $"Template '{name}' reset to default.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
