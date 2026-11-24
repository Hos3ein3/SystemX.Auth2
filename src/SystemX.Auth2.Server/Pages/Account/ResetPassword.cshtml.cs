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

public sealed class ResetPasswordModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{

    /// <summary>The login (email or phone) from the link.</summary>
    [BindProperty(SupportsGet = true)] public string? Login { get; set; }
    /// <summary>The token from the link, or the code from the SMS.</summary>
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, DataType(DataType.Password), StringLength(1024)] public string NewPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadProjectAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadProjectAsync()) return Page();
        if (!ModelState.IsValid || string.IsNullOrEmpty(Login) || string.IsNullOrEmpty(Token)) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        var result = await Svc<IPasswordService>().ResetAsync(Login, Token, Input.NewPassword, HttpContext.RequestAborted);
        Input.NewPassword = "";
        if (!result.Succeeded) { Fail(result); return Page(); }
        return RedirectToPage("Login", new { ReturnUrl, ClientId = ClientIdForLinks });
    }
}
