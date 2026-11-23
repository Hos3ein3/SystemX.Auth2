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

public sealed class VerifyOtpModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{

    [BindProperty] public InputModel Input { get; set; } = new();
    public bool CodeSent { get; private set; }
    public bool ShowEmailLogin { get; private set; }
    public bool ShowPhoneField { get; private set; }

    public sealed class InputModel
    {
        /// <summary>Email or phone number.</summary>
        [Required, StringLength(256)] public string Login { get; set; } = "";
        [StringLength(16)] public string? Code { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await Prepare();
        return Page();
    }

    /// <summary>Send a code. The page says the same thing whether the account exists or not.</summary>
    public async Task<IActionResult> OnPostSendAsync()
    {
        if (!await Prepare()) return Page();
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        var result = await Svc<IOtpRequestService>().RequestAsync(Input.Login, Auth2OtpPurposes.Login, HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result); return Page(); }
        CodeSent = true;
        return Page();
    }

    /// <summary>Check the code and sign in.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await Prepare()) return Page();
        if (!ModelState.IsValid || string.IsNullOrEmpty(Input.Code)) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        var result = await Svc<ILoginService>().OtpLoginAsync(new OtpLoginRequest(Input.Login, Input.Code,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString()), HttpContext.RequestAborted);
        if (result.Succeeded) return await SignInAndRedirectAsync(result, Settings.Redirects.RedirectAfterLogin);
        if (result.ErrorCode == Auth2ErrorCodes.LockedOut) return RedirectToPage("Lockout", new { ReturnUrl, ClientId = Application!.ClientId });
        Fail(result.ErrorCode ?? Auth2ErrorCodes.InvalidOtp);
        return Page();
    }

    private async Task<bool> Prepare()
    {
        if (!await LoadApplicationAsync()) return false;
        ShowEmailLogin = Settings.Methods.AllowedLoginMethods.Contains(LoginMethod.EmailOtp);
        ShowPhoneField = Settings.Methods.AllowedLoginMethods.Contains(LoginMethod.PhoneOtp);
        return true;
    }
}
