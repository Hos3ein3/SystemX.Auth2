using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Applications;

public sealed class CreateModel : AdminPageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public string? CreatedSecret { get; private set; }
    public ApplicationDto? CreatedApp { get; private set; }

    public sealed class InputModel
    {
        [Required, RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Application name must be lowercase alphanumeric with hyphens."), StringLength(64, MinimumLength = 2)]
        public string Name { get; set; } = "";

        [StringLength(100)]
        public string? ClientId { get; set; }

        public string? RedirectUris { get; set; }
        public string? PostLogoutUris { get; set; }
        public string? AllowedScopes { get; set; } = "openid profile email";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        if (!ModelState.IsValid) return Page();

        var redirectList = (Input.RedirectUris ?? "").Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries);
        var postLogoutList = (Input.PostLogoutUris ?? "").Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries);
        var scopeList = (Input.AllowedScopes ?? "").Split(['\r', '\n', ' ', ','], StringSplitOptions.RemoveEmptyEntries);

        var appSvc = Svc<IAdminApplicationService>();
        var serverUrl = $"{Request.Scheme}://{Request.Host}";

        var (result, response) = await appSvc.CreateAsync(CurrentProject!.Id, new CreateApplicationRequest(
            Input.Name,
            string.IsNullOrWhiteSpace(Input.ClientId) ? null : Input.ClientId,
            redirectList,
            postLogoutList,
            scopeList
        ), serverUrl, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Failed to create application");
            return Page();
        }

        CreatedSecret = response!.ClientSecret;
        CreatedApp = response.Application;
        return Page();
    }
}
