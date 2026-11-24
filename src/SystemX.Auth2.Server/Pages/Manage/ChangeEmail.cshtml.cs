using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Pages.Manage;

public sealed class ChangeEmailModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? CurrentEmail { get; private set; }
    public bool EmailSent { get; private set; }

    public sealed class InputModel
    {
        [Required, EmailAddress, StringLength(256)]
        public string NewEmail { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        CurrentEmail = CurrentUser!.Email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        CurrentEmail = CurrentUser!.Email;
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        if (string.Equals(CurrentUser.Email, Input.NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            Fail(Auth2ErrorCodes.ValidationFailed);
            return Page();
        }

        // Send confirmation email
        var confirmSvc = Svc<IConfirmationService>();
        await confirmSvc.ResendEmailAsync(Input.NewEmail, "/Manage/Index", HttpContext.RequestAborted);
        EmailSent = true;
        return Page();
    }
}
