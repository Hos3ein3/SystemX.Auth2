#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Pages.Account.External;

/// <summary>
/// The address the provider sends the browser back to. The URL (project slug and provider name) decides which provider and application this is.
/// POST is allowed without an anti-forgery token because the provider sends it (Apple, SAML); the sealed state cookie and the single-use state protect it.
/// </summary>
[IgnoreAntiforgeryToken]
public sealed class CallbackModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text, ExternalFlow external)
    : Auth2PageModel(clientId, flow, urls, text)
{
    public Task<IActionResult> OnGetAsync(string slug, string name) => HandleAsync(slug, name);

    public Task<IActionResult> OnPostAsync(string slug, string name) => HandleAsync(slug, name);

    private async Task<IActionResult> HandleAsync(string slug, string name)
    {
        var result = await external.CompleteAsync(HttpContext, slug, name, HttpContext.RequestAborted);
        await LoadProjectAsync();   // the flow already chose the application from the URL
        if (result.RedirectTo is not null) return Redirect(result.RedirectTo);
        if (result.TwoFactorToken is not null)
        {
            TempData["TwoFactorToken"] = result.TwoFactorToken;
            return RedirectToPage("/Account/TwoFactor", new { ReturnUrl = result.ReturnUrl, ClientId = ClientIdForLinks });
        }
        if (result.PendingToken is not null)
        {
            TempData["ExternalPending"] = result.PendingToken;
            return RedirectToPage("/Account/External/Confirm", new { ReturnUrl = result.ReturnUrl, ClientId = ClientIdForLinks });
        }
        if (result.ErrorCode == Auth2ErrorCodes.LockedOut) return RedirectToPage("/Account/Lockout", new { ReturnUrl, ClientId = ClientIdForLinks });
        Fail(result.ErrorCode ?? Auth2ErrorCodes.ExternalClaimsInvalid);
        return Page();
    }
}
