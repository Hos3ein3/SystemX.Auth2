using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Global;

public sealed class KeysModel(IKeyRing keyRing) : PageModel
{
    public IReadOnlyList<KeyInfo> Keys { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return Forbid();

        Keys = await keyRing.ListAsync(HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostRotateAsync()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return Forbid();

        await keyRing.RotateAsync(HttpContext.RequestAborted);
        TempData["Success"] = "Signing and encryption keys rotated. New active keys generated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetireAsync(Guid id)
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return Forbid();

        var retired = await keyRing.RetireAsync(id, HttpContext.RequestAborted);
        if (retired) TempData["Success"] = "Key retired early.";
        else TempData["Error"] = "Failed to retire key.";
        return RedirectToPage();
    }
}
