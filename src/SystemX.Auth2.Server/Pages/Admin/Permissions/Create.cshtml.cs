using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Permissions;

public sealed class CreateModel : AdminPageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, RegularExpression("^[a-z0-9_.-]+$", ErrorMessage = "Permission key must be lowercase alphanumeric with dots, hyphens, or underscores."), StringLength(100, MinimumLength = 3)]
        public string Key { get; set; } = "";

        [StringLength(100)]
        public string? DisplayName { get; set; }

        [StringLength(256)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string? Group { get; set; }
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

        var accessSvc = Svc<IAccessService>();
        var result = await accessSvc.CreatePermissionAsync(new PermissionDefinition(
            Input.Key, Input.DisplayName, Input.Description, Input.Group
        ), HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Failed to create permission");
            return Page();
        }

        TempData["Success"] = $"Permission '{Input.Key}' created successfully.";
        return Redirect($"/admin/p/{ProjectKey}/permissions");
    }
}
