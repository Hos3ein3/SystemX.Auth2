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

public sealed class RecoveryCodesModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{

    [BindProperty] public InputModel Input { get; set; } = new();
    /// <summary>New codes. Shown once.</summary>
    public IReadOnlyList<string> Codes { get; private set; } = [];

    public sealed class InputModel
    {
        [Required, StringLength(16)] public string Code { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadSessionAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        var (result, codes) = await Svc<ITwoFactorService>().RegenerateRecoveryCodesAsync(CurrentUser!, Input.Code, HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result); return Page(); }
        await RefreshSessionAsync(CurrentUser!, codeGivenNow: true);   // a right code of the authenticator app
        Codes = codes;
        return Page();
    }
}
