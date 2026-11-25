#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>
/// A signed-in person changes their own phone number in two steps. <b>Step 1</b> (handler <c>SendCode</c>): the new number is checked and an SMS with a
/// code goes to the <b>new</b> number. <b>Step 2</b> (post): the code is checked and the number is replaced and marked as confirmed.
/// The code works once, only for that person and only for that number; the attempts, the lifetime and the delay between two codes follow <c>TwoFactor.Otp*</c>.
/// </summary>
public sealed class ChangePhoneNumberModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : AccountChangePageModel(clientId, flow, urls, text)
{
    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>The phone number now.</summary>
    public string? CurrentPhoneNumber { get; private set; }

    /// <summary>True after step 1: the code was sent to the new number. The UI then shows the field for the code (with the same <c>Input.NewPhoneNumber</c>).</summary>
    public bool CodeSent { get; private set; }

    /// <summary>True after step 2: the number was replaced.</summary>
    public bool Changed { get; private set; }

    public sealed class InputModel
    {
        [Required, StringLength(32)] public string NewPhoneNumber { get; set; } = "";
        [DataType(DataType.Password), StringLength(1024)] public string? CurrentPassword { get; set; }
        [StringLength(16)] public string? Code { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync()) CurrentPhoneNumber = CurrentUser!.PhoneNumber;
        return Page();
    }

    /// <summary>Step 1: check the new number and send the code to it.</summary>
    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        if (!await LoadAsync()) return Page();
        CurrentPhoneNumber = CurrentUser!.PhoneNumber;
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        var result = await Svc<IAccountChangeService>().RequestPhoneChangeAsync(CurrentUser, Input.NewPhoneNumber, Input.CurrentPassword, HttpContext.RequestAborted);
        if (!result.Succeeded) { FailWith(result); return Page(); }
        CodeSent = true;
        return Page();
    }

    /// <summary>Step 2: check the code and replace the number.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return Page();
        CurrentPhoneNumber = CurrentUser!.PhoneNumber;
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Input.Code)) { Fail(Auth2ErrorCodes.ValidationFailed); CodeSent = true; return Page(); }

        var result = await Svc<IAccountChangeService>().ConfirmPhoneChangeAsync(CurrentUser, Input.NewPhoneNumber, Input.Code, HttpContext.RequestAborted);
        if (!result.Succeeded) { FailWith(result); CodeSent = true; return Page(); }

        CurrentPhoneNumber = CurrentUser.PhoneNumber;
        Changed = true;
        return Page();
    }
}
