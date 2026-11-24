using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Scopes;

public sealed class DetailModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public AdminScopeDto? Scope { get; private set; }

    [BindProperty] public UpdateInputModel Input { get; set; } = new();

    public sealed class UpdateInputModel
    {
        [StringLength(100)]
        public string? DisplayName { get; set; }

        [StringLength(256)]
        public string? Description { get; set; }

        public string? Resources { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var scopeSvc = Svc<IAdminScopeService>();
        Scope = await scopeSvc.GetAsync(Id, HttpContext.RequestAborted);
        if (Scope is null) return NotFound();

        Input = new UpdateInputModel
        {
            DisplayName = Scope.DisplayName,
            Description = Scope.Description,
            Resources = string.Join(" ", Scope.Resources)
        };
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var resList = (Input.Resources ?? "").Split(['\r', '\n', ' ', ','], StringSplitOptions.RemoveEmptyEntries);

        var scopeSvc = Svc<IAdminScopeService>();
        var (result, updated) = await scopeSvc.UpdateAsync(Id, new UpdateScopeRequest(
            Input.DisplayName, Input.Description, resList
        ), IsGlobalAdmin, HttpContext.RequestAborted);

        if (!result.Succeeded) TempData["Error"] = "Failed to update scope.";
        else TempData["Success"] = "Scope settings saved.";

        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var scopeSvc = Svc<IAdminScopeService>();
        var result = await scopeSvc.DeleteAsync(Id, IsGlobalAdmin, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.ErrorCode ?? "Could not delete scope.";
            return RedirectToPage(new { projectKey = ProjectKey, id = Id });
        }

        TempData["Success"] = "Scope deleted.";
        return Redirect($"/admin/p/{ProjectKey}/scopes");
    }
}
