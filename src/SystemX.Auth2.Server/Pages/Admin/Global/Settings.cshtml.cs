using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Global;

public sealed class SettingsModel(IOptions<Auth2ServerOptions> options) : PageModel
{
    public Auth2ServerOptions ServerOptions { get; private set; } = options.Value;

    public IActionResult OnGet()
    {
        if (!User.IsInRole("GlobalAdmin") && !User.IsInRole("Developer") && !User.HasClaim("scope", "auth2.admin.global"))
            return NotFound();

        ServerOptions = options.Value;
        return Page();
    }
}
