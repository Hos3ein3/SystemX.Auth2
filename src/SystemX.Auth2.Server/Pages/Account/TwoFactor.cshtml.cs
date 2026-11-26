using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>Second step of the sign-in: a TOTP code or a recovery code.</summary>
public sealed class TwoFactorModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text, ILoginService login)
    : Auth2PageModel(clientId, flow, urls, text)
{
    /// <summary>Short-lived token from the first step. Keep it in a hidden field.</summary>
    [BindProperty] public string TwoFactorToken { get; set; } = "";

    /// <summary>Form input.</summary>
    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>Show the "use a recovery code" option.</summary>
    public bool ShowRecoveryCode { get; private set; }

    /// <summary>The form fields. Send a code or a recovery code.</summary>
    public sealed class InputModel
    {
        /// <summary>Code from the authenticator app.</summary>
        [StringLength(16)] public string? Code { get; set; }

        /// <summary>One recovery code.</summary>
        [StringLength(64)] public string? RecoveryCode { get; set; }
    }

    /// <summary>Loads the token from the first step.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadProjectAsync()) return Page();
        ShowRecoveryCode = Settings.TwoFactor.AllowRecoveryCodes;
        TwoFactorToken = TempData.Peek("TwoFactorToken") as string ?? "";
        if (TwoFactorToken.Length == 0) return RedirectToPage("Login", new { ReturnUrl, ClientId });
        return Page();
    }

    /// <summary>Checks the code and signs the user in.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadProjectAsync()) return Page();
        ShowRecoveryCode = Settings.TwoFactor.AllowRecoveryCodes;

        var result = await login.CompleteTwoFactorAsync(TwoFactorToken, Input.Code, Input.RecoveryCode,
            HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            TempData.Remove("TwoFactorToken");
            // the second step is a code of the authenticator app (or a recovery code): the restricted pages do not ask again at once
            await Auth2Cookie.SignInAsync(HttpContext, result.User!, Project!, Client, Effective, result.Amr, result.SessionId, stepUpAtUtc: DateTimeOffset.UtcNow);
            return Redirect(ResolveRedirect(Settings.Redirects.RedirectAfterLogin));
        }
        if (result.ErrorCode == Auth2ErrorCodes.LockedOut) return RedirectToPage("Lockout", new { ReturnUrl, ClientId });
        Fail(result.ErrorCode ?? Auth2ErrorCodes.InvalidOtp);
        return Page();
    }
}
