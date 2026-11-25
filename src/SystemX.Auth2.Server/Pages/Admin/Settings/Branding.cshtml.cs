using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Settings;

public sealed class BrandingModel : AdminPageModel
{
    public IReadOnlyList<BrandingAssetDto> Assets { get; private set; } = [];

    [BindProperty] public string PrimaryColor { get; set; } = "#2563eb";
    [BindProperty] public string PrimaryTextColor { get; set; } = "#ffffff";
    [BindProperty] public string BackgroundColor { get; set; } = "#f9fafb";
    [BindProperty] public string Radius { get; set; } = "0.375rem";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var brandingSvc = Svc<IAdminBrandingService>();
        Assets = await brandingSvc.ListAssetsAsync(CurrentProject!.Id, HttpContext.RequestAborted);

        var projectSvc = Svc<IAdminProjectService>();
        var settingsTuple = await projectSvc.GetSettingsAsync(CurrentProject.Id, HttpContext.RequestAborted);
        if (settingsTuple?.Settings.Branding is { } b)
        {
            if (!string.IsNullOrWhiteSpace(b.PrimaryColor)) PrimaryColor = b.PrimaryColor;
            if (!string.IsNullOrWhiteSpace(b.PrimaryTextColor)) PrimaryTextColor = b.PrimaryTextColor;
            if (!string.IsNullOrWhiteSpace(b.BackgroundColor)) BackgroundColor = b.BackgroundColor;
            if (!string.IsNullOrWhiteSpace(b.Radius)) Radius = b.Radius;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUploadAssetAsync(string kind, IFormFile? file)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a valid image file to upload.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var brandingSvc = Svc<IAdminBrandingService>();
        var (result, asset) = await brandingSvc.UploadAssetAsync(CurrentProject!.Id, kind, file.ContentType, bytes, HttpContext.RequestAborted);

        if (!result.Succeeded) TempData["Error"] = result.ErrorCode ?? "Failed to upload asset.";
        else TempData["Success"] = $"Asset '{kind}' uploaded.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostDeleteAssetAsync(string kind)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var brandingSvc = Svc<IAdminBrandingService>();
        var result = await brandingSvc.DeleteAssetAsync(CurrentProject!.Id, kind, HttpContext.RequestAborted);

        if (!result.Succeeded) TempData["Error"] = "Failed to delete asset.";
        else TempData["Success"] = $"Asset '{kind}' deleted.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostSaveColorsAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var projectSvc = Svc<IAdminProjectService>();
        var settingsTuple = await projectSvc.GetSettingsAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        if (settingsTuple is null)
        {
            TempData["Error"] = "Project settings could not be loaded.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var settings = settingsTuple.Value.Settings;
        settings.Branding ??= new();
        settings.Branding.PrimaryColor = string.IsNullOrWhiteSpace(PrimaryColor) ? "#2563eb" : PrimaryColor.Trim();
        settings.Branding.PrimaryTextColor = string.IsNullOrWhiteSpace(PrimaryTextColor) ? "#ffffff" : PrimaryTextColor.Trim();
        settings.Branding.BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor) ? "#f9fafb" : BackgroundColor.Trim();
        settings.Branding.Radius = string.IsNullOrWhiteSpace(Radius) ? "0.375rem" : Radius.Trim();

        var (result, _) = await projectSvc.UpdateSettingsAsync(
            CurrentProject.Id,
            settings,
            IsGlobalAdmin,
            settingsTuple.Value.Version,
            HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            var msg = result.Errors?.Count > 0
                ? string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
                : (result.ErrorCode ?? "Failed to save branding color tokens.");
            TempData["Error"] = msg;
        }
        else
        {
            TempData["Success"] = "Branding color tokens saved successfully.";
        }

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
