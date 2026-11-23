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
namespace SystemX.Auth2.Server.Pages.Manage;

public sealed class ChangePasswordModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{

    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Changed { get; private set; }

    public sealed class InputModel
    {
        [Required, DataType(DataType.Password), StringLength(1024)] public string CurrentPassword { get; set; } = "";
        [Required, DataType(DataType.Password), StringLength(1024)] public string NewPassword { get; set; } = "";
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
        var result = await Svc<IPasswordService>().ChangeAsync(CurrentUser!, Input.CurrentPassword, Input.NewPassword, keepAuthorizationId: null, HttpContext.RequestAborted);
        Input.CurrentPassword = Input.NewPassword = "";
        if (!result.Succeeded) { Fail(result); return Page(); }
        Changed = true;
        return Page();
    }
}
