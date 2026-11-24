#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Pages.Account.External;

/// <summary>
/// Step for sign-ins where the provider gave no email and the application needs a confirmed one.
/// POST asks for the email and sends a confirmation mail. The link of the mail (<c>ConfirmationToken</c>) finishes the sign-in.
/// </summary>
public sealed class ConfirmModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text, IExternalLoginService external, ExternalFlow externalFlow)
    : Auth2PageModel(clientId, flow, urls, text)
{
    /// <summary>The sealed identity from the callback (hidden field of the form).</summary>
    [BindProperty] public string? PendingToken { get; set; }
    /// <summary>The email the user typed.</summary>
    [BindProperty, EmailAddress, StringLength(256)] public string? Email { get; set; }
    /// <summary>The token of the confirmation mail.</summary>
    [BindProperty(SupportsGet = true)] public string? ConfirmationToken { get; set; }
    /// <summary>True after the mail was sent.</summary>
    public bool EmailSent { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadProjectAsync()) return Page();
        if (!string.IsNullOrEmpty(ConfirmationToken))
        {
            var outcome = await external.ConfirmEmailAsync(ConfirmationToken, HttpContext.RequestAborted);
            var result = await externalFlow.FinishAsync(HttpContext, Project!, Settings, outcome, HttpContext.RequestAborted);
            if (result.RedirectTo is not null) return Redirect(result.RedirectTo);
            if (result.TwoFactorToken is not null)
            {
                TempData["TwoFactorToken"] = result.TwoFactorToken;
                return RedirectToPage("/Account/TwoFactor", new { ReturnUrl = result.ReturnUrl, ClientId = ClientIdForLinks });
            }
            Fail(result.ErrorCode ?? Auth2ErrorCodes.InvalidToken);
            return Page();
        }
        PendingToken = TempData["ExternalPending"] as string;
        TempData.Keep("ExternalPending");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadProjectAsync()) return Page();
        if (string.IsNullOrEmpty(PendingToken) || string.IsNullOrWhiteSpace(Email) || !ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        var url = Url.Page("/Account/External/Confirm")!;
        EmailSent = await external.SendEmailConfirmationAsync(PendingToken, Email, url, HttpContext.RequestAborted);
        if (!EmailSent) Fail(Auth2ErrorCodes.InvalidToken);
        return Page();   // the same answer for every address
    }
}
