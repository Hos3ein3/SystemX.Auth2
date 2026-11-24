using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Projects;

public sealed class DetailModel(IAdminProjectService projectService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public ProjectDto? Project { get; private set; }

    [BindProperty] public UpdateInputModel Input { get; set; } = new();
    [BindProperty] public DeleteInputModel DeleteInput { get; set; } = new();

    public sealed class UpdateInputModel
    {
        [Required, StringLength(100, MinimumLength = 3)]
        public string Name { get; set; } = "";
        [StringLength(500)]
        public string? Description { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? Reason { get; set; }
    }

    public sealed class DeleteInputModel
    {
        public string ConfirmationKey { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return NotFound();

        Project = await projectService.GetAsync(Id, HttpContext.RequestAborted);
        if (Project is null) return NotFound();

        Input = new UpdateInputModel
        {
            Name = Project.Name,
            Description = Project.Description,
            IsEnabled = Project.IsEnabled
        };
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return NotFound();

        Project = await projectService.GetAsync(Id, HttpContext.RequestAborted);
        if (Project is null) return NotFound();

        var (result, updated) = await projectService.UpdateAsync(Id, new UpdateProjectRequest(Input.Name, Input.Description, Input.IsEnabled, Input.Reason), null, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Update failed");
            return Page();
        }

        TempData["Success"] = "Project details updated.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return NotFound();

        Project = await projectService.GetAsync(Id, HttpContext.RequestAborted);
        if (Project is null) return NotFound();

        if (Project.ProjectKey == "_global")
        {
            ModelState.AddModelError("", "The _global project cannot be deleted.");
            return Page();
        }

        if (DeleteInput.ConfirmationKey != Project.ProjectKey)
        {
            ModelState.AddModelError("", "Confirmation key does not match.");
            return Page();
        }

        var result = await projectService.DeleteAsync(Id, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Delete failed");
            return Page();
        }

        TempData["Success"] = $"Project '{Project.Name}' has been permanently deleted.";
        return Redirect("/admin/projects");
    }
}
