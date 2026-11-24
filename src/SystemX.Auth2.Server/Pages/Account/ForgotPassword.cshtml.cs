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

public sealed class ForgotPasswordModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{

    [BindProperty] public InputModel Input { get; set; } = new();
    /// <summary>True after the form was sent. The page says the same thing for every login: "if the account exists, we sent a message".</summary>
    public bool Submitted { get; private set; }

    public sealed class InputModel
    {
        [Required, StringLength(256)] public string Login { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadProjectAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadProjectAsync()) return Page();
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        await Svc<IPasswordService>().ForgotAsync(Input.Login, SafeReturnUrl(), HttpContext.RequestAborted);
        Submitted = true;
        return Page();
    }
}
