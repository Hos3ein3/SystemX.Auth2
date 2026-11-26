using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Branding;

namespace SystemX.Auth2.Server.Pages.Admin.Settings;

public sealed class BrandingModel : AdminPageModel
{
    public IReadOnlyList<BrandingAssetDto> Assets { get; private set; } = [];

    // Brand & Identity
    [BindProperty] public string? BrandName { get; set; }
    [BindProperty] public string? SupportEmail { get; set; }

    // Primary Colors
    [BindProperty] public string PrimaryColor { get; set; } = "#2563eb";
    [BindProperty] public string PrimaryTextColor { get; set; } = "#ffffff";

    // Secondary Colors
    [BindProperty] public string SecondaryColor { get; set; } = "#4f46e5";
    [BindProperty] public string SecondaryTextColor { get; set; } = "#ffffff";

    // Surfaces & Canvas
    [BindProperty] public string BackgroundColor { get; set; } = "#f9fafb";
    [BindProperty] public string SurfaceColor { get; set; } = "#ffffff";

    // Typography Colors & Shape Tokens
    [BindProperty] public string TextColor { get; set; } = "#111827";
    [BindProperty] public string MutedTextColor { get; set; } = "#6b7280";
    [BindProperty] public string Radius { get; set; } = "0.375rem";
    [BindProperty] public string FontFamily { get; set; } = "system-ui, -apple-system, sans-serif";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var brandingSvc = Svc<IAdminBrandingService>();
        Assets = await brandingSvc.ListAssetsAsync(CurrentProject!.Id, HttpContext.RequestAborted);

        // Ensure database assets are synced to wwwroot/projects/{projectname}/
        var env = Svc<IWebHostEnvironment>();
        var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        foreach (var a in Assets)
        {
            if (ProjectAssetStorage.FindAssetUrl(webRoot, CurrentProject.ProjectKey, a.Kind) is null)
            {
                var fullAsset = await brandingSvc.GetAssetAsync(CurrentProject.Id, a.Kind, HttpContext.RequestAborted);
                if (fullAsset?.Data.Length > 0)
                {
                    await ProjectAssetStorage.SaveAssetAsync(webRoot, CurrentProject.ProjectKey, CurrentProject.Name, a.Kind, fullAsset.ContentType, fullAsset.Data);
                }
            }
        }

        var projectSvc = Svc<IAdminProjectService>();
        var settingsTuple = await projectSvc.GetSettingsAsync(CurrentProject.Id, HttpContext.RequestAborted);
        if (settingsTuple?.Settings is { } s)
        {
            if (s.Localization is { } loc)
            {
                BrandName = loc.BrandName;
                SupportEmail = loc.SupportEmail;
            }

            if (s.Branding is { } b)
            {
                if (!string.IsNullOrWhiteSpace(b.PrimaryColor)) PrimaryColor = b.PrimaryColor;
                if (!string.IsNullOrWhiteSpace(b.PrimaryTextColor)) PrimaryTextColor = b.PrimaryTextColor;
                if (!string.IsNullOrWhiteSpace(b.SecondaryColor)) SecondaryColor = b.SecondaryColor;
                if (!string.IsNullOrWhiteSpace(b.SecondaryTextColor)) SecondaryTextColor = b.SecondaryTextColor;
                if (!string.IsNullOrWhiteSpace(b.BackgroundColor)) BackgroundColor = b.BackgroundColor;
                if (!string.IsNullOrWhiteSpace(b.SurfaceColor)) SurfaceColor = b.SurfaceColor;
                if (!string.IsNullOrWhiteSpace(b.TextColor)) TextColor = b.TextColor;
                if (!string.IsNullOrWhiteSpace(b.MutedTextColor)) MutedTextColor = b.MutedTextColor;
                if (!string.IsNullOrWhiteSpace(b.Radius)) Radius = b.Radius;
                if (!string.IsNullOrWhiteSpace(b.FontFamily)) FontFamily = b.FontFamily;
            }
        }

        if (string.IsNullOrWhiteSpace(BrandName))
        {
            BrandName = CurrentProject.Name;
        }

        return Page();
    }

    public string? GetDiskAssetUrl(string kind)
    {
        var env = Svc<IWebHostEnvironment>();
        var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        return ProjectAssetStorage.FindAssetUrl(webRoot, CurrentProject?.ProjectKey ?? ProjectKey, kind);
    }

    public async Task<IActionResult> OnPostUploadAssetAsync(string kind, IFormFile? file)
    {
        if (!await AuthorizeAdminAsync()) return Page();
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a valid image file to upload.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var normalizedKind = kind?.Trim().ToLowerInvariant() switch
        {
            "logo" or "logo_light" or "logo-light" => "logo",
            "logo_dark" or "logo-dark" => "logo-dark",
            "favicon" => "favicon",
            "background" => "background",
            _ => kind?.Trim() ?? "asset"
        };

        var maxBytes = normalizedKind switch
        {
            "favicon" => 256 * 1024,
            "background" => 5 * 1024 * 1024,
            _ => 3 * 1024 * 1024
        };

        if (file.Length > maxBytes)
        {
            TempData["Error"] = $"File size ({file.Length / 1024} KB) exceeds maximum allowed size for '{normalizedKind}' ({maxBytes / 1024} KB).";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var contentType = file.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream")
        {
            contentType = Path.GetExtension(file.FileName).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".webp" => "image/webp",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "image/png"
            };
        }

        var brandingSvc = Svc<IAdminBrandingService>();
        var (result, _) = await brandingSvc.UploadAssetAsync(CurrentProject!.Id, normalizedKind, contentType, bytes, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Failed to upload asset.";
        }
        else
        {
            // Under wwwroot, create/update folder /projects/{projectname}/ and always replace the asset
            var env = Svc<IWebHostEnvironment>();
            var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var savedPath = await ProjectAssetStorage.SaveAssetAsync(
                webRoot,
                CurrentProject.ProjectKey,
                CurrentProject.Name,
                normalizedKind,
                contentType,
                bytes,
                file.FileName);

            TempData["Success"] = $"Asset '{normalizedKind}' saved to '{savedPath}'.";
        }

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostDeleteAssetAsync(string kind)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var normalizedKind = kind?.Trim().ToLowerInvariant() switch
        {
            "logo" or "logo_light" or "logo-light" => "logo",
            "logo_dark" or "logo-dark" => "logo-dark",
            "favicon" => "favicon",
            "background" => "background",
            _ => kind?.Trim() ?? ""
        };

        var brandingSvc = Svc<IAdminBrandingService>();
        var result = await brandingSvc.DeleteAssetAsync(CurrentProject!.Id, normalizedKind, HttpContext.RequestAborted);

        // Remove from disk under wwwroot/projects/{projectname}/
        var env = Svc<IWebHostEnvironment>();
        var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        ProjectAssetStorage.DeleteAsset(webRoot, CurrentProject.ProjectKey, CurrentProject.Name, normalizedKind);

        if (!result.Succeeded) TempData["Error"] = "Failed to delete asset.";
        else TempData["Success"] = $"Asset '{normalizedKind}' deleted successfully.";

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public Task<IActionResult> OnPostSaveTokensAsync() => SaveTokensInternalAsync();

    public Task<IActionResult> OnPostSaveColorsAsync() => SaveTokensInternalAsync();

    private async Task<IActionResult> SaveTokensInternalAsync()
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
        settings.Localization ??= new();
        if (!string.IsNullOrWhiteSpace(BrandName))
        {
            settings.Localization.BrandName = BrandName.Trim();
        }
        settings.Localization.SupportEmail = string.IsNullOrWhiteSpace(SupportEmail) ? null : SupportEmail.Trim();

        settings.Branding ??= new();
        settings.Branding.PrimaryColor = string.IsNullOrWhiteSpace(PrimaryColor) ? "#2563eb" : PrimaryColor.Trim();
        settings.Branding.PrimaryTextColor = string.IsNullOrWhiteSpace(PrimaryTextColor) ? "#ffffff" : PrimaryTextColor.Trim();
        settings.Branding.SecondaryColor = string.IsNullOrWhiteSpace(SecondaryColor) ? "#4f46e5" : SecondaryColor.Trim();
        settings.Branding.SecondaryTextColor = string.IsNullOrWhiteSpace(SecondaryTextColor) ? "#ffffff" : SecondaryTextColor.Trim();
        settings.Branding.BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor) ? "#f9fafb" : BackgroundColor.Trim();
        settings.Branding.SurfaceColor = string.IsNullOrWhiteSpace(SurfaceColor) ? "#ffffff" : SurfaceColor.Trim();
        settings.Branding.TextColor = string.IsNullOrWhiteSpace(TextColor) ? "#111827" : TextColor.Trim();
        settings.Branding.MutedTextColor = string.IsNullOrWhiteSpace(MutedTextColor) ? "#6b7280" : MutedTextColor.Trim();
        settings.Branding.Radius = string.IsNullOrWhiteSpace(Radius) ? "0.375rem" : Radius.Trim();
        settings.Branding.FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "system-ui, -apple-system, sans-serif" : FontFamily.Trim();

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
                : (result.ErrorCode ?? "Failed to save branding visual tokens.");
            TempData["Error"] = msg;
        }
        else
        {
            TempData["Success"] = "Branding visual tokens and settings saved successfully.";
        }

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
