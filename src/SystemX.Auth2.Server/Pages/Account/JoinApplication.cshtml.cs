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

public sealed class JoinApplicationModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{

    [BindProperty] public InputModel Input { get; set; } = new();
    public bool CodeSent { get; private set; }
    /// <summary>The join flow is open for this application (<c>JoinMode=AutoJoin</c>, multi mode).</summary>
    public bool JoinEnabled { get; private set; }
    /// <summary>With shared credentials the person keeps their password and no new one is asked.</summary>
    public bool AskNewPassword { get; private set; }

    public sealed class InputModel
    {
        [Required, StringLength(256)] public string Login { get; set; } = "";
        [StringLength(16)] public string? Code { get; set; }
        [DataType(DataType.Password), StringLength(1024)] public string? NewPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await Prepare();
        return Page();
    }

    /// <summary>Step 1: send the proof code to the confirmed email or phone. The page says the same thing for every login.</summary>
    public async Task<IActionResult> OnPostSendAsync()
    {
        if (!await Prepare()) return Page();
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        await Svc<IJoinService>().RequestAsync(Input.Login, HttpContext.RequestAborted);
        CodeSent = true;
        return Page();
    }

    /// <summary>Step 2: the code and the new password for this application.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await Prepare()) return Page();
        if (!ModelState.IsValid || string.IsNullOrEmpty(Input.Code)) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        var result = await Svc<IJoinService>().CompleteAsync(Input.Login, Input.Code, Input.NewPassword ?? "", HttpContext.RequestAborted);
        Input.NewPassword = null;
        if (!result.Succeeded) { Fail(result); return Page(); }
        return RedirectToPage("Login", new { ReturnUrl, ClientId = Application!.ClientId });
    }

    private async Task<bool> Prepare()
    {
        if (!await LoadApplicationAsync()) return false;
        JoinEnabled = Settings.Registration.JoinMode == Domain.Settings.JoinMode.AutoJoin
            && Svc<Microsoft.Extensions.Options.IOptions<Auth2ServerOptions>>().Value.Mode == ServerMode.MultiApplication;
        AskNewPassword = await Svc<IAuthFlowResolver>().GetCredentialScopeAsync(HttpContext.RequestAborted) == CredentialScope.PerApplication;
        return true;
    }
}
