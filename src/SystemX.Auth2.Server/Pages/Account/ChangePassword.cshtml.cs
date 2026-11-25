#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>
/// A signed-in person changes their own password. Needs the current password. Every token session of the person in this application ends.
/// With <c>Password.SignOutAfterChangePassword</c> on, this session ends too: the cookie is removed and the person goes to the sign-in page.
/// </summary>
public sealed class ChangePasswordModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : AccountChangePageModel(clientId, flow, urls, text)
{
    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>True after the password was changed and the person stays signed in.</summary>
    public bool Changed { get; private set; }

    public sealed class InputModel
    {
        [Required, DataType(DataType.Password), StringLength(1024)] public string CurrentPassword { get; set; } = "";
        [Required, DataType(DataType.Password), StringLength(1024)] public string NewPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return Page();
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        // The hosted-UI cookie is not a token, so there is no token session of this page to keep: every token of the person ends.
        // The cookie stays (the person keeps using the pages) unless Password.SignOutAfterChangePassword says that nothing stays.
        var result = await Svc<IPasswordService>().ChangeAsync(CurrentUser!, Input.CurrentPassword, Input.NewPassword, keepAuthorizationId: null, HttpContext.RequestAborted);
        Input.CurrentPassword = Input.NewPassword = "";
        if (!result.Succeeded) { Fail(result); return Page(); }

        if (Settings.Password.SignOutAfterChangePassword)
        {
            await SignOutCookieAsync();
            return RedirectToPage("Login", new { ReturnUrl = SafeReturnUrl(), ClientId = ClientIdForLinks });
        }
        Changed = true;
        return Page();
    }
}
