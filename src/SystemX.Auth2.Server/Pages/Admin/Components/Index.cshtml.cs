using Microsoft.AspNetCore.Mvc;

namespace SystemX.Auth2.Server.Pages.Admin.Components;

public sealed class IndexModel : AdminPageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        return Page();
    }
}
