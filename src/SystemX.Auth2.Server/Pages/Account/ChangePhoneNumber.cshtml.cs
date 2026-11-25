#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
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

        var newNumber = Input.NewPhoneNumber.Trim();
        if (string.Equals(newNumber, CurrentUser.PhoneNumber, StringComparison.Ordinal) && CurrentUser.PhoneNumberConfirmed) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }
        if (!await ConfirmWithPasswordAsync(Input.CurrentPassword)) return Page();
        if (!await ValidateChangeAsync(phoneNumber: newNumber)) return Page();

        var issued = await Svc<IOtpService>().IssueAsync(CurrentUser, PurposeFor(newNumber), HttpContext.RequestAborted);
        if (issued.Code is null) { Fail(Auth2ErrorCodes.RateLimited, StatusCodes.Status429TooManyRequests); return Page(); }   // a code was sent a moment ago
        await Svc<IUserNotifier>().SendPhoneChangeCodeAsync(CurrentUser, newNumber, issued.Code, HttpContext.RequestAborted);
        await AuditAsync("phone.change_requested");
        CodeSent = true;
        return Page();
    }

    /// <summary>Step 2: check the code and replace the number.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return Page();
        CurrentPhoneNumber = CurrentUser!.PhoneNumber;
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Input.Code)) { Fail(Auth2ErrorCodes.ValidationFailed); CodeSent = true; return Page(); }

        var newNumber = Input.NewPhoneNumber.Trim();
        if (!await ValidateChangeAsync(phoneNumber: newNumber)) { CodeSent = true; return Page(); }
        if (await Svc<IOtpService>().VerifyAsync(CurrentUser, PurposeFor(newNumber), Input.Code.Trim(), HttpContext.RequestAborted) != OtpVerifyResult.Ok)
        {
            Fail(Auth2ErrorCodes.InvalidOtp);
            CodeSent = true;
            return Page();
        }

        var users = Svc<UserManager<AppUser>>();
        CurrentUser.PhoneNumber = newNumber;
        CurrentUser.PhoneNumberConfirmed = true;   // the code proved that the person holds this number
        var result = await users.UpdateAsync(CurrentUser);
        if (!result.Succeeded) { Fail(OperationResult.Fail(result.Errors)); return Page(); }

        await AuditAsync("phone.changed");
        CurrentPhoneNumber = CurrentUser.PhoneNumber;
        Changed = true;
        return Page();
    }

    /// <summary>The purpose of the code is tied to the number, so a code sent for one number cannot confirm another.</summary>
    private static string PurposeFor(string phoneNumber) =>
        Auth2OtpPurposes.ChangePhone + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phoneNumber)))[..16].ToLowerInvariant();
}
