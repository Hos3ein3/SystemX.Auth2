using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Applications;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public ApplicationDto? App { get; private set; }
    public string? RotatedSecret { get; private set; }

    [BindProperty] public UpdateInputModel Input { get; set; } = new();

    public sealed class UpdateInputModel
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string? RedirectUris { get; set; }
        public string? PostLogoutUris { get; set; }
        public string? AllowedScopes { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var appSvc = Svc<IAdminApplicationService>();
        App = await appSvc.GetAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
        if (App is null) return NotFound();

        Input = new UpdateInputModel
        {
            Name = App.Name,
            IsEnabled = App.IsEnabled,
            RedirectUris = string.Join("\n", App.RedirectUris),
            PostLogoutUris = string.Join("\n", App.PostLogoutRedirectUris),
            AllowedScopes = string.Join(" ", App.AllowedScopes)
        };
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var redirectList = (Input.RedirectUris ?? "").Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries);
        var postLogoutList = (Input.PostLogoutUris ?? "").Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries);
        var scopeList = (Input.AllowedScopes ?? "").Split(['\r', '\n', ' ', ','], StringSplitOptions.RemoveEmptyEntries);

        var appSvc = Svc<IAdminApplicationService>();
        var (result, updated) = await appSvc.UpdateAsync(CurrentProject!.Id, Id, new UpdateApplicationRequest(
            Input.Name,
            Input.IsEnabled,
            redirectList,
            postLogoutList,
            scopeList
        ), null, HttpContext.RequestAborted);

        if (!result.Succeeded) TempData["Error"] = "Failed to update application.";
        else TempData["Success"] = "Application settings saved.";

        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }

    public async Task<IActionResult> OnPostRotateSecretAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var appSvc = Svc<IAdminApplicationService>();
        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        var (result, setup) = await appSvc.RotateSecretAsync(CurrentProject!.Id, Id, IsGlobalAdmin, serverUrl, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not rotate secret.";
            return RedirectToPage(new { projectKey = ProjectKey, id = Id });
        }

        TempData["Success"] = "Client secret rotated.";
        RotatedSecret = setup?.ClientSecret;

        App = await appSvc.GetAsync(CurrentProject.Id, Id, HttpContext.RequestAborted);
        Input = new UpdateInputModel
        {
            Name = App!.Name,
            IsEnabled = App.IsEnabled,
            RedirectUris = string.Join("\n", App.RedirectUris),
            PostLogoutUris = string.Join("\n", App.PostLogoutRedirectUris),
            AllowedScopes = string.Join(" ", App.AllowedScopes)
        };

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

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
