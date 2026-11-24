using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Providers;

public sealed class CreateModel : AdminPageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, RegularExpression("^[a-z0-9_-]+$", ErrorMessage = "Provider name must be lowercase alphanumeric with hyphens or underscores."), StringLength(64, MinimumLength = 2)]
        public string Name { get; set; } = "";

        [Required]
        public string Type { get; set; } = "Oidc";

        [StringLength(100)]
        public string? DisplayName { get; set; }

        public string? AuthorityUrl { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? Scopes { get; set; } = "openid profile email";
        public bool Enabled { get; set; } = true;
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

        var bodyObj = new Dictionary<string, object?>
        {
            ["name"] = Input.Name,
            ["type"] = Input.Type,
            ["displayName"] = Input.DisplayName ?? Input.Name,
            ["enabled"] = Input.Enabled,
            ["authority"] = Input.AuthorityUrl,
            ["clientId"] = Input.ClientId,
            ["clientSecret"] = Input.ClientSecret,
            ["scopes"] = Input.Scopes
        };
        var jsonBody = JsonSerializer.Serialize(bodyObj);

        var externalSvc = Svc<IAdminExternalService>();
        var (result, provider) = await externalSvc.CreateProviderAsync(CurrentProject!.Id, jsonBody, IsGlobalAdmin, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Failed to create provider");
            return Page();
        }

        TempData["Success"] = $"Provider '{Input.Name}' added successfully.";
        return Redirect($"/admin/p/{ProjectKey}/providers/detail?name={Uri.EscapeDataString(Input.Name)}");
    }
}
