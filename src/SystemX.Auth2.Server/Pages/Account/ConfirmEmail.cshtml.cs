#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Pages.Account;

public sealed class ConfirmEmailModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{

    [BindProperty(SupportsGet = true)] public Guid UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Confirmed { get; private set; }
    /// <summary>After success: the safe return URL, or the login page.</summary>
    public string? ContinueUrl { get; private set; }
    public bool ResendSent { get; private set; }

    public sealed class InputModel
    {
        [Required, StringLength(256)] public string Login { get; set; } = "";
    }

    /// <summary>The link in the email opens this page. A GET changes data here on purpose: the token is the proof and works once.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadApplicationAsync()) return Page();
        if (UserId == Guid.Empty || string.IsNullOrEmpty(Token)) { Fail(Auth2ErrorCodes.InvalidToken); return Page(); }
        var result = await Svc<IConfirmationService>().ConfirmEmailAsync(UserId, Token, HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result); return Page(); }
        Confirmed = true;
        ContinueUrl = SafeReturnUrl() ?? Url.Page("Login", new { ReturnUrl, ClientId = Application!.ClientId });
        return Page();
    }

    /// <summary>Send the link again. The page says the same thing for every login.</summary>
    public async Task<IActionResult> OnPostResendAsync()
    {
        if (!await LoadApplicationAsync()) return Page();
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        await Svc<IConfirmationService>().ResendEmailAsync(Input.Login, SafeReturnUrl(), HttpContext.RequestAborted);
        ResendSent = true;
        return Page();
    }
}
