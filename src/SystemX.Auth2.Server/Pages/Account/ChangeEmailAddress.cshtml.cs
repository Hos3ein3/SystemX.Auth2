#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>
/// A signed-in person changes their own email address in two steps. <b>Step 1</b> (post): the new address is checked, and a confirmation link goes
/// to the <b>new</b> address. Nothing changes yet. <b>Step 2</b> (the link, handler <c>Confirm</c>): the address is replaced and marked as confirmed.
/// The link works once, only for that person and only for that address.
/// </summary>
public sealed class ChangeEmailAddressModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : AccountChangePageModel(clientId, flow, urls, text)
{
    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>The email address now.</summary>
    public string? CurrentEmail { get; private set; }

    /// <summary>True after step 1: the link was sent to the new address.</summary>
    public bool EmailSent { get; private set; }

    /// <summary>True after step 2: the address was replaced.</summary>
    public bool Changed { get; private set; }

    public sealed class InputModel
    {
        [Required, EmailAddress, StringLength(256)] public string NewEmail { get; set; } = "";
        [DataType(DataType.Password), StringLength(1024)] public string? CurrentPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync()) CurrentEmail = CurrentUser!.Email;
        return Page();
    }

    /// <summary>Step 1: check the new address and send the confirmation link to it.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return Page();
        CurrentEmail = CurrentUser!.Email;
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        var result = await Svc<IAccountChangeService>().RequestEmailChangeAsync(CurrentUser, Input.NewEmail, Input.CurrentPassword, SafeReturnUrl(), HttpContext.RequestAborted);
        if (!result.Succeeded) { FailWith(result); return Page(); }
        EmailSent = true;
        return Page();
    }

    /// <summary>The address of the link in the email (handler <c>Confirm</c>).</summary>
    [BindProperty(SupportsGet = true)] public Guid UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Email { get; set; }
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }

    /// <summary>
    /// Step 2: the link in the email. A GET changes data here on purpose (like <c>ConfirmEmail</c>): the token is the proof, and it works once.
    /// The signed-in person must be the person of the link.
    /// </summary>
    public async Task<IActionResult> OnGetConfirmAsync()
    {
        if (!await LoadAsync()) return Page();
        CurrentEmail = CurrentUser!.Email;
        if (UserId != CurrentUser.Id || string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Token)) { Fail(Auth2ErrorCodes.InvalidToken); return Page(); }

        var result = await Svc<IAccountChangeService>().ConfirmEmailChangeAsync(CurrentUser, Email, Token, HttpContext.RequestAborted);
        if (!result.Succeeded) { FailWith(result); return Page(); }
        CurrentEmail = CurrentUser.Email;
        Changed = true;
        return Page();
    }
}
