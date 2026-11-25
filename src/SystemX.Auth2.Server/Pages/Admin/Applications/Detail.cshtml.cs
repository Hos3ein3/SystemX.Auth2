using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Pages.Admin.Applications;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public ApplicationDto? App { get; private set; }
    public ClientApplicationSettings? AppSettings { get; private set; }
    public string? RotatedSecret { get; private set; }
    public IReadOnlyList<ExternalProviderAdminDto> AvailableProviders { get; private set; } = [];

    public bool CanEditGlobalOnly => IsGlobalAdmin || User.IsInRole("GlobalAdmin") || User.IsInRole("Developer");
    public bool CanEditProjectAdmin => CanEditGlobalOnly || User.IsInRole("ProjectAdmin") || User.IsInRole("Admin");

    [BindProperty] public UpdateInputModel Input { get; set; } = new();

    public sealed class UpdateInputModel
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string? RedirectUris { get; set; }
        public string? PostLogoutUris { get; set; }
        public string? AllowedScopes { get; set; }
        public SystemX.Auth2.Contracts.Enums.ApplicationType ApplicationType { get; set; }
        [StringLength(500)] public string? WebServerUrl { get; set; }

        // ClientApplicationSettings overrides:
        public UiMode? UiMode { get; set; }
        public bool AllowSelfUiLogin { get; set; }
        public bool AllowClientToSetWebServerUrl { get; set; } = true;
        public int? AccessTokenMinutes { get; set; }
        public int? RefreshTokenDays { get; set; }
        public int? IdentityTokenMinutes { get; set; }
        public int? CookieLifetimeMinutes { get; set; }
        public string? AllowedCorsOrigins { get; set; }
        public ConsentMode? ConsentMode { get; set; }
        public bool? SendConfirmationEmail { get; set; }
        public bool? SendConfirmationSMS { get; set; }
        public string? ExternalProviders { get; set; }
    }

    private void EnsureIdResolved()
    {
        if (Id != Guid.Empty) return;
        if (Guid.TryParse(Request.Query["id"], out var qId) && qId != Guid.Empty) Id = qId;
        else if (Guid.TryParse(Request.Form["Id"], out var fId) && fId != Guid.Empty) Id = fId;
        else if (Guid.TryParse(Request.Form["id"], out var fId2) && fId2 != Guid.Empty) Id = fId2;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return NotFound();

        EnsureIdResolved();
        if (Id == Guid.Empty) return NotFound();
        var appSvc = Svc<IAdminApplicationService>();
        App = await appSvc.GetAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
        if (App is null) return NotFound();

        var (_, settings, _) = await appSvc.GetSettingsAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
        AppSettings = settings;

        var externalSvc = Svc<IAdminExternalService>();
        AvailableProviders = await externalSvc.ListProvidersAsync(CurrentProject!.Id, HttpContext.RequestAborted) ?? [];

        Input = new UpdateInputModel
        {
            Name = App.Name,
            IsEnabled = App.IsEnabled,
            RedirectUris = string.Join("\n", App.RedirectUris),
            PostLogoutUris = string.Join("\n", App.PostLogoutRedirectUris),
            AllowedScopes = string.Join(" ", App.AllowedScopes),
            ApplicationType = App.ApplicationType,
            WebServerUrl = App.WebServerUrl,
            UiMode = settings?.UiMode,
            AllowSelfUiLogin = settings?.AllowSelfUiLogin ?? false,
            AllowClientToSetWebServerUrl = settings?.AllowClientToSetWebServerUrl ?? true,
            AccessTokenMinutes = settings?.AccessTokenMinutes,
            RefreshTokenDays = settings?.RefreshTokenDays,
            IdentityTokenMinutes = settings?.IdentityTokenMinutes,
            CookieLifetimeMinutes = settings?.CookieLifetimeMinutes,
            AllowedCorsOrigins = settings?.AllowedCorsOrigins is not null ? string.Join("\n", settings.AllowedCorsOrigins) : null,
            ConsentMode = settings?.ConsentMode,
            SendConfirmationEmail = settings?.SendConfirmationEmail,
            SendConfirmationSMS = settings?.SendConfirmationSMS,
            ExternalProviders = settings?.ExternalProviders is not null ? string.Join(", ", settings.ExternalProviders) : null
        };
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!await AuthorizeAdminAsync()) return NotFound();
        EnsureIdResolved();
        if (Id == Guid.Empty) return NotFound();

        var redirectList = (Input.RedirectUris ?? "").Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries);
        var postLogoutList = (Input.PostLogoutUris ?? "").Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries);
        var scopeList = (Input.AllowedScopes ?? "").Split(['\r', '\n', ' ', ','], StringSplitOptions.RemoveEmptyEntries);

        var appSvc = Svc<IAdminApplicationService>();
        var (result, updated) = await appSvc.UpdateAsync(CurrentProject!.Id, Id, new UpdateApplicationRequest(
            Input.Name,
            Input.IsEnabled,
            redirectList,
            postLogoutList,
            scopeList,
            ApplicationType: IsGlobalAdmin ? Input.ApplicationType : null,
            WebServerUrl: IsGlobalAdmin ? Input.WebServerUrl ?? "" : null
        ), null, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            TempData["Error"] = "Failed to update application.";
            return Redirect($"/admin/p/{ProjectKey}/applications/detail?id={Id}");
        }

        var corsOrigins = (Input.AllowedCorsOrigins ?? "")
            .Split(['\r', '\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var extProviders = string.IsNullOrWhiteSpace(Input.ExternalProviders)
            ? null
            : Input.ExternalProviders
                .Split([',', '\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        var clientSettings = new ClientApplicationSettings
        {
            UiMode = Input.UiMode,
            AllowSelfUiLogin = Input.AllowSelfUiLogin,
            AllowClientToSetWebServerUrl = Input.AllowClientToSetWebServerUrl,
            AccessTokenMinutes = Input.AccessTokenMinutes,
            RefreshTokenDays = Input.RefreshTokenDays,
            IdentityTokenMinutes = Input.IdentityTokenMinutes,
            CookieLifetimeMinutes = Input.CookieLifetimeMinutes,
            AllowedCorsOrigins = corsOrigins,
            ConsentMode = Input.ConsentMode,
            SendConfirmationEmail = Input.SendConfirmationEmail,
            SendConfirmationSMS = Input.SendConfirmationSMS,
            ExternalProviders = extProviders
        };

        if (!CanEditGlobalOnly)
        {
            var (_, currentSettings, _) = await appSvc.GetSettingsAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
            if (currentSettings is not null)
            {
                clientSettings.SchemaVersion = currentSettings.SchemaVersion;
                clientSettings.UiMode = currentSettings.UiMode;
                clientSettings.CookieLifetimeMinutes = currentSettings.CookieLifetimeMinutes;
                clientSettings.AccessTokenMinutes = currentSettings.AccessTokenMinutes;
                clientSettings.RefreshTokenDays = currentSettings.RefreshTokenDays;
                clientSettings.IdentityTokenMinutes = currentSettings.IdentityTokenMinutes;
                clientSettings.AllowedCorsOrigins = currentSettings.AllowedCorsOrigins;
                clientSettings.AllowSelfUiLogin = currentSettings.AllowSelfUiLogin;
                clientSettings.AllowClientToSetWebServerUrl = currentSettings.AllowClientToSetWebServerUrl;
                clientSettings.ConsentMode = currentSettings.ConsentMode;
            }
        }

        var (settingsResult, _) = await appSvc.UpdateSettingsAsync(
            CurrentProject!.Id,
            Id,
            clientSettings,
            CanEditGlobalOnly,
            null,
            HttpContext.RequestAborted);

        if (!settingsResult.Succeeded)
        {
            var msg = settingsResult.Errors?.Count > 0
                ? string.Join("; ", settingsResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
                : (settingsResult.ErrorCode ?? "Failed to update application settings.");
            TempData["Error"] = msg;
            return Redirect($"/admin/p/{ProjectKey}/applications/detail?id={Id}");
        }

        TempData["Success"] = "Application settings saved.";
        return Redirect($"/admin/p/{ProjectKey}/applications/detail?id={Id}");
    }

    public async Task<IActionResult> OnPostRotateSecretAsync()
    {
        if (!await AuthorizeAdminAsync()) return NotFound();
        EnsureIdResolved();
        if (Id == Guid.Empty) return NotFound();

        var appSvc = Svc<IAdminApplicationService>();
        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        var (result, setup) = await appSvc.RotateSecretAsync(CurrentProject!.Id, Id, IsGlobalAdmin, serverUrl, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not rotate secret.";
            return Redirect($"/admin/p/{ProjectKey}/applications/detail?id={Id}");
        }

        TempData["Success"] = "Client secret rotated.";
        RotatedSecret = setup?.ClientSecret;

        App = await appSvc.GetAsync(CurrentProject.Id, Id, HttpContext.RequestAborted);
        var (_, settings, _) = await appSvc.GetSettingsAsync(CurrentProject.Id, Id, HttpContext.RequestAborted);
        AppSettings = settings;

        Input = new UpdateInputModel
        {
            Name = App!.Name,
            IsEnabled = App.IsEnabled,
            RedirectUris = string.Join("\n", App.RedirectUris),
            PostLogoutUris = string.Join("\n", App.PostLogoutRedirectUris),
            AllowedScopes = string.Join(" ", App.AllowedScopes),
            ApplicationType = App.ApplicationType,
            WebServerUrl = App.WebServerUrl,
            UiMode = settings?.UiMode,
            AllowSelfUiLogin = settings?.AllowSelfUiLogin ?? false,
            AllowClientToSetWebServerUrl = settings?.AllowClientToSetWebServerUrl ?? true,
            AccessTokenMinutes = settings?.AccessTokenMinutes,
            RefreshTokenDays = settings?.RefreshTokenDays,
            IdentityTokenMinutes = settings?.IdentityTokenMinutes,
            CookieLifetimeMinutes = settings?.CookieLifetimeMinutes,
            AllowedCorsOrigins = settings?.AllowedCorsOrigins is not null ? string.Join("\n", settings.AllowedCorsOrigins) : null,
            ConsentMode = settings?.ConsentMode,
            SendConfirmationEmail = settings?.SendConfirmationEmail,
            SendConfirmationSMS = settings?.SendConfirmationSMS
        };

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return NotFound();
        EnsureIdResolved();
        if (Id == Guid.Empty) return NotFound();

        var appSvc = Svc<IAdminApplicationService>();
        var result = await appSvc.DeleteAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not delete application.";
            return RedirectToPage(new { projectKey = ProjectKey, id = Id });
        }

        TempData["Success"] = "Application deleted.";
        return Redirect($"/admin/p/{ProjectKey}/applications");
    }
}
