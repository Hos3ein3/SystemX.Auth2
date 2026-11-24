using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Pages.Admin.Settings;

public sealed class UiModel : AdminPageModel
{
    [BindProperty] public UiInputModel Input { get; set; } = new();

    public sealed class UiInputModel
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Identifier { get; set; } = "EmailOrUsername";
        public string LayoutVariant { get; set; } = "centered";
        public bool ShowRememberMe { get; set; } = true;
        public bool ShowForgotPassword { get; set; } = true;
        public bool ShowRegisterLink { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        Input = new UiInputModel
        {
            Title = CurrentProject?.Name ?? "Sign in",
            Subtitle = "Welcome back",
            Identifier = "EmailOrUsername",
            LayoutVariant = "centered",
            ShowRememberMe = true,
            ShowForgotPassword = true,
            ShowRegisterLink = true
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        TempData["Success"] = "UI & Login page settings saved.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
