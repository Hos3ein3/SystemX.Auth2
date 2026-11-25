#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>
/// A signed-in person changes their own user name. The new name must follow the project rules (<c>UserName.*</c>) and be free.
/// A person who has a password gives it again.
/// </summary>
public sealed class ChangeUserNameModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : AccountChangePageModel(clientId, flow, urls, text)
{
    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>The user name now.</summary>
    public string? CurrentUserName { get; private set; }

    /// <summary>True after the user name was changed.</summary>
    public bool Changed { get; private set; }

    public sealed class InputModel
    {
        [Required, StringLength(256)] public string NewUserName { get; set; } = "";
        [DataType(DataType.Password), StringLength(1024)] public string? CurrentPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync()) CurrentUserName = CurrentUser!.UserName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return Page();
        CurrentUserName = CurrentUser!.UserName;
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        var result = await Svc<IAccountChangeService>().ChangeUserNameAsync(CurrentUser, Input.NewUserName, Input.CurrentPassword, HttpContext.RequestAborted);
        if (!result.Succeeded) { FailWith(result); return Page(); }

        await RefreshCookieAsync(CurrentUser);
        CurrentUserName = CurrentUser.UserName;
        Changed = true;
        return Page();
    }
}
