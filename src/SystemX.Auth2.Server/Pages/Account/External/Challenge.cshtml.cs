#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Pages.Account.External;

/// <summary>Starts a sign-in at an external provider. POST only (anti-forgery token needed).</summary>
public sealed class ChallengeModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text, ExternalFlow external)
    : Auth2PageModel(clientId, flow, urls, text)
{
    [BindProperty] public string Provider { get; set; } = "";
    [BindProperty] public string? Hint { get; set; }

    public IActionResult OnGet() => RedirectToPage("/Account/Login", new { ReturnUrl, ClientId });

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadApplicationAsync()) return Page();
        var (redirect, error) = await external.StartAsync(HttpContext, Provider, ReturnUrl, Hint, null, HttpContext.RequestAborted);
        if (redirect is null)
        {
            Fail(error ?? Auth2ErrorCodes.ExternalProviderDisabled);
            return Page();
        }
        return Redirect(redirect);
    }
}
