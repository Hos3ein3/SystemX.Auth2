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

public sealed class ConfirmPhoneModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{

    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Confirmed { get; private set; }
    public bool CodeSent { get; private set; }
    public string? ContinueUrl { get; private set; }

    public sealed class InputModel
    {
        [Required, StringLength(256)] public string Login { get; set; } = "";
        [StringLength(16)] public string? Code { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadProjectAsync();
        return Page();
    }

    /// <summary>Send a new code.</summary>
    public async Task<IActionResult> OnPostSendAsync()
    {
        if (!await LoadProjectAsync()) return Page();
        var result = await Svc<IOtpRequestService>().RequestAsync(Input.Login, Auth2OtpPurposes.ConfirmPhone, HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result); return Page(); }
        CodeSent = true;
        return Page();
    }

    /// <summary>Check the code.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadProjectAsync()) return Page();
        if (!ModelState.IsValid || string.IsNullOrEmpty(Input.Code)) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        var result = await Svc<IConfirmationService>().ConfirmPhoneAsync(Input.Login, Input.Code, HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result); return Page(); }
        Confirmed = true;
        ContinueUrl = SafeReturnUrl() ?? Url.Page("Login", new { ReturnUrl, ClientId = ClientIdForLinks });
        return Page();
    }
}
