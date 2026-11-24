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

public sealed class RegisterModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{

    [BindProperty] public InputModel Input { get; set; } = new();
    public bool ShowUserNameField { get; private set; }
    public bool ShowEmailField { get; private set; }
    public bool ShowPhoneField { get; private set; }
    public bool RegistrationEnabled { get; private set; }
    /// <summary>Offer "I already have an account on this server" (the join page). Shown to everybody, so it tells nothing about a person.</summary>
    public bool ShowJoinLink { get; private set; }

    public sealed class InputModel
    {
        [StringLength(64)] public string? UserName { get; set; }
        [EmailAddress, StringLength(256)] public string? Email { get; set; }
        [StringLength(32)] public string? PhoneNumber { get; set; }
        [DataType(DataType.Password), StringLength(1024)] public string? Password { get; set; }
        [StringLength(100)] public string? FirstName { get; set; }
        [StringLength(100)] public string? LastName { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await PrepareAsync()) return Page();
        if (!RegistrationEnabled) { Fail(Auth2ErrorCodes.RegistrationDisabled, StatusCodes.Status403Forbidden); return Page(); }
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        var outcome = await Svc<IRegisterService>().RegisterAsync(new RegisterRequest(Input.UserName, Input.Email, Input.PhoneNumber,
            Input.Password ?? "", Input.FirstName, Input.LastName, SafeReturnUrl()), SafeReturnUrl(), HttpContext.RequestAborted);
        Input.Password = null;
        if (!outcome.Result.Succeeded) { Fail(outcome.Result); return Page(); }

        // No sign-in when something must be confirmed first. A person who already existed gets the same answer as a new one
        // (the page cannot tell them apart), and goes to the login page with the return URL kept.
        if (outcome.UserWasCreated && !Settings.Registration.LoginRequiredAfterRegister && !outcome.ConfirmationRequired)
            return await SignInAndRedirectAsync(new LoginResult { Succeeded = true, User = outcome.User, Amr = ["pwd"], SessionId = Guid.NewGuid().ToString("N") },
                Settings.Redirects.RedirectAfterRegister);
        return RedirectToPage("Login", new { ReturnUrl, ClientId = ClientIdForLinks });
    }

    private async Task<bool> PrepareAsync()
    {
        if (!await LoadProjectAsync()) return false;
        var m = Settings.Methods.AllowedRegisterMethods;
        ShowUserNameField = m.Contains(LoginMethod.UserNamePassword);
        ShowEmailField = m.Contains(LoginMethod.EmailPassword) || m.Contains(LoginMethod.EmailOtp);
        ShowPhoneField = m.Contains(LoginMethod.PhonePassword) || m.Contains(LoginMethod.PhoneOtp);
        RegistrationEnabled = Settings.Registration.RegistrationEnabled;
        ShowJoinLink = Settings.Registration.JoinMode == Domain.Settings.JoinMode.AutoJoin;
        return true;
    }
}
