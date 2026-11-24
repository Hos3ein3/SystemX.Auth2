using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>Sign-in page of the hosted UI. All rules are in <see cref="ILoginService"/>.</summary>
public sealed class LoginModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text, ILoginService login, UserManager<AppUser> users)
    : Auth2PageModel(clientId, flow, urls, text)
{
    /// <summary>Form input.</summary>
    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>Show a user name field (login with user name).</summary>
    public bool ShowUserNameLogin { get; private set; }
    /// <summary>Show an email field.</summary>
    public bool ShowEmailLogin { get; private set; }
    /// <summary>Show a phone field.</summary>
    public bool ShowPhoneField { get; private set; }
    /// <summary>Show a link to the register page.</summary>
    public bool ShowRegisterLink { get; private set; }

    /// <summary>True when developer login prompt modal should be shown.</summary>
    public bool ShowDeveloperModal { get; private set; }

    /// <summary>Target redirect url (RedirectAfterLogin).</summary>
    public string RedirectAfterLoginUrl { get; private set; } = "/";

    /// <summary>Developer dashboard url.</summary>
    public string DeveloperDashboardUrl { get; private set; } = "/";

    /// <summary>The form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>User name, email or phone number.</summary>
        [Required, StringLength(256)] public string Login { get; set; } = "";

        /// <summary>Password. Never sent back to the page.</summary>
        [Required, DataType(DataType.Password), StringLength(1024)] public string Password { get; set; } = "";
    }

    /// <summary>Loads the flags.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        await PrepareAsync();
        return Page();
    }

    /// <summary>Signs the user in.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await PrepareAsync()) return Page();
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        var result = await login.PasswordLoginAsync(new PasswordLoginRequest(Input.Login, Input.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString()), HttpContext.RequestAborted);
        Input.Password = "";

        if (result.Succeeded)
        {
            await Auth2Cookie.SignInAsync(HttpContext, result.User!, Project!, Client, Effective, result.Amr, result.SessionId);
            return Redirect(ResolveRedirect(Settings.Redirects.RedirectAfterLogin));
        }
        switch (result.ErrorCode)
        {
            case Auth2ErrorCodes.TwoFactorRequired:
                TempData["TwoFactorToken"] = result.TwoFactorToken;
                return RedirectToPage("TwoFactor", new { ReturnUrl, ClientId = ClientIdForLinks });
            case Auth2ErrorCodes.LockedOut:
                return RedirectToPage("Lockout", new { ReturnUrl, ClientId = ClientIdForLinks });
            default:
                Fail(result.ErrorCode ?? Auth2ErrorCodes.InvalidCredentials, StatusCodes.Status400BadRequest);
                return Page();
        }
    }

    private async Task<bool> PrepareAsync()
    {
        if (!await LoadProjectAsync()) return false;
        var methods = Settings.Methods.AllowedLoginMethods;
        ShowUserNameLogin = methods.Contains(LoginMethod.UserNamePassword);
        ShowEmailLogin = methods.Contains(LoginMethod.EmailPassword);
        ShowPhoneField = methods.Contains(LoginMethod.PhonePassword) || methods.Contains(LoginMethod.PhoneOtp);
        ShowRegisterLink = Settings.Registration.RegistrationEnabled;
        return true;
    }
}
