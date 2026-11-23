#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Pages.Manage;
namespace SystemX.Auth2.Server.Pages.Manage.TwoFactor;

public sealed class EnableModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{

    [BindProperty] public InputModel Input { get; set; } = new();
    /// <summary>Key to type into the authenticator app.</summary>
    public string? SharedKey { get; private set; }
    /// <summary><c>otpauth://</c> URI for a QR code.</summary>
    public string? AuthenticatorUri { get; private set; }
    public bool AlreadyEnabled { get; private set; }
    /// <summary>The recovery codes. Shown once, right after turning two-factor on.</summary>
    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];
    public bool Enabled { get; private set; }
    public bool CanDisable { get; private set; }

    public sealed class InputModel
    {
        [StringLength(16)] public string? Code { get; set; }
        [DataType(DataType.Password), StringLength(1024)] public string? Password { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        await LoadSetupAsync();
        return Page();
    }

    /// <summary>Turn on, with the first code from the app.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        if (string.IsNullOrEmpty(Input.Code)) { Fail(Auth2ErrorCodes.ValidationFailed); await LoadSetupAsync(keepKey: true); return Page(); }
        var (result, codes) = await Svc<ITwoFactorService>().EnableAsync(CurrentUser!, Input.Code, HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result); await LoadSetupAsync(keepKey: true); return Page(); }
        Enabled = true;
        RecoveryCodes = codes;
        CanDisable = Settings.TwoFactor.Mode != Domain.Settings.TwoFactorMode.Required;
        return Page();
    }

    /// <summary>Turn off. Needs the password. Not possible when the application requires two-factor.</summary>
    public async Task<IActionResult> OnPostDisableAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        var result = await Svc<ITwoFactorService>().DisableAsync(CurrentUser!, Input.Password ?? "", HttpContext.RequestAborted);
        Input.Password = null;
        if (!result.Succeeded) { Fail(result); return Page(); }
        return RedirectToPage();
    }

    private async Task LoadSetupAsync(bool keepKey = false)
    {
        CanDisable = Settings.TwoFactor.Mode != Domain.Settings.TwoFactorMode.Required;
        var (result, setup) = keepKey ? (OperationResult.Ok, null) : await Svc<ITwoFactorService>().SetupAsync(CurrentUser!, HttpContext.RequestAborted);
        AlreadyEnabled = result.ErrorCode == Auth2ErrorCodes.Conflict;
        if (setup is not null) (SharedKey, AuthenticatorUri) = (setup.SharedKey, setup.AuthenticatorUri);
    }
}
