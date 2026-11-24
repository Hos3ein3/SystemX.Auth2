using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Projects;

public sealed class CreateModel(IAdminProjectService projectService) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Project key must be lowercase alphanumeric with hyphens."), StringLength(40, MinimumLength = 2)]
        public string ProjectKey { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 3)]
        public string Name { get; set; } = "";

        [StringLength(500)]
        public string? Description { get; set; }
    }

    public IActionResult OnGet()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return Forbid();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return Forbid();

        if (!ModelState.IsValid) return Page();

        var (result, response) = await projectService.CreateAsync(new CreateProjectRequest(Input.ProjectKey, Input.Name, Input.Description), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Failed to create project");
            return Page();
        }

        TempData["Success"] = $"Project '{Input.Name}' created successfully.";
        return Redirect($"/admin/p/{Input.ProjectKey}");
    }
}
