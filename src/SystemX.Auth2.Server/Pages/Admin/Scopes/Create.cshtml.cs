using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Scopes;

public sealed class CreateModel : AdminPageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, RegularExpression(@"^[^\s\\\""]+$", ErrorMessage = "Scope name cannot contain spaces, double quotes, or backslashes."), StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = "";

        [StringLength(100)]
        public string? DisplayName { get; set; }

        [StringLength(256)]
        public string? Description { get; set; }

        public string? Resources { get; set; }
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

        var resList = (Input.Resources ?? "").Split(['\r', '\n', ' ', ','], StringSplitOptions.RemoveEmptyEntries);

        var scopeSvc = Svc<IAdminScopeService>();
        var (result, scope) = await scopeSvc.CreateAsync(new CreateScopeRequest(
            Input.Name, Input.DisplayName, Input.Description, resList
        ), IsGlobalAdmin, HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Failed to create scope");
            return Page();
        }

        TempData["Success"] = $"Scope '{Input.Name}' created successfully.";
        return Redirect($"/admin/p/{ProjectKey}/scopes");
    }
}
