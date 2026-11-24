using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Groups;

public sealed class CreateModel : AdminPageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, RegularExpression("^[a-z0-9_-]+$", ErrorMessage = "Group name must be lowercase alphanumeric with hyphens or underscores."), StringLength(64, MinimumLength = 2)]
        public string Name { get; set; } = "";

        [StringLength(100)]
        public string? DisplayName { get; set; }

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

        var groupSvc = Svc<IGroupService>();
        var result = await groupSvc.CreateAsync(Input.Name, Input.DisplayName, Input.Description, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Failed to create group");
            return Page();
        }

        TempData["Success"] = $"Group '{Input.Name}' created successfully.";
        return Redirect($"/admin/p/{ProjectKey}/groups/detail?name={Uri.EscapeDataString(Input.Name)}");
    }
}
