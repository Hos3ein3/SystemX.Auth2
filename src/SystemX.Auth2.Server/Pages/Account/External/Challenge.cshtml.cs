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

    /// <summary>For providers that need a form post (SAML POST binding): the address and fields. The UI renders an auto-submitting form.</summary>
    public string? PostAction { get; private set; }
    public IReadOnlyDictionary<string, string>? PostFields { get; private set; }

    public IActionResult OnGet() => RedirectToPage("/Account/Login", new { ReturnUrl, ClientId });

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadApplicationAsync()) return Page();
        var start = await external.StartAsync(HttpContext, Provider, ReturnUrl, Hint, null, HttpContext.RequestAborted);
        if (!start.Succeeded)
        {
            Fail(start.ErrorCode ?? Auth2ErrorCodes.ExternalProviderDisabled);
            return Page();
        }
        if (start.PostFields is not null)
        {
            PostAction = start.RedirectTo;
            PostFields = start.PostFields;
            return Page();
        }
        return Redirect(start.RedirectTo!);
    }
}
