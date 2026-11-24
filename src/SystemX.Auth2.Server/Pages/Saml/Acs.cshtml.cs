#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Pages.Saml;

/// <summary>
/// SAML assertion consumer service: the identity provider posts the response here. No anti-forgery token (the identity provider cannot have one);
/// the sealed state cookie, the single-use request id and the signature checks protect it.
/// </summary>
[IgnoreAntiforgeryToken]
public sealed class AcsModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text, ExternalFlow external)
    : Auth2PageModel(clientId, flow, urls, text)
{
    public IActionResult OnGet() => StatusCode(StatusCodes.Status405MethodNotAllowed);

    public async Task<IActionResult> OnPostAsync(string slug, string name)
    {
        var result = await external.CompleteAsync(HttpContext, slug, name, HttpContext.RequestAborted);
        await LoadApplicationAsync();
        if (result.RedirectTo is not null) return Redirect(result.RedirectTo);
        if (result.TwoFactorToken is not null)
        {
            TempData["TwoFactorToken"] = result.TwoFactorToken;
            return RedirectToPage("/Account/TwoFactor", new { ReturnUrl = result.ReturnUrl, ClientId = Application?.ClientId });
        }
        if (result.PendingToken is not null)
        {
            TempData["ExternalPending"] = result.PendingToken;
            return RedirectToPage("/Account/External/Confirm", new { ReturnUrl = result.ReturnUrl, ClientId = Application?.ClientId });
        }
        if (result.ErrorCode == Auth2ErrorCodes.LockedOut) return RedirectToPage("/Account/Lockout", new { ClientId = Application?.ClientId });
        Fail(result.ErrorCode ?? Auth2ErrorCodes.SamlResponseInvalid);
        return Page();
    }
}
