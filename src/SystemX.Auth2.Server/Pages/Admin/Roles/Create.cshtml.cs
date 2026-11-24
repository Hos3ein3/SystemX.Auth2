using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Roles;

public sealed class CreateModel : AdminPageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, RegularExpression("^[a-zA-Z0-9_-]+$", ErrorMessage = "Role name can only contain letters, numbers, underscores, and hyphens."), StringLength(64, MinimumLength = 2)]
        public string Name { get; set; } = "";

        [StringLength(256)]
        public string? Description { get; set; }
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
        var result = await accessSvc.CreateRoleAsync(Input.Name, Input.Description, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Failed to create role");
            return Page();
        }

        TempData["Success"] = $"Role '{Input.Name}' created successfully.";
        return Redirect($"/admin/p/{ProjectKey}/roles/detail?name={Uri.EscapeDataString(Input.Name)}");
    }
}
