using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Pages.Manage;

public sealed class DeleteAccountModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        if (await RequireRecentTotpAsync() is { } ask) return ask;   // a restricted page: a code of the authenticator app first
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        if (await RequireRecentTotpAsync(Request.Path.Value) is { } ask) return ask;
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        var userManager = Svc<UserManager<AppUser>>();
        var validPassword = await userManager.CheckPasswordAsync(CurrentUser!, Input.Password);
        if (!validPassword)
        {
            Fail(Auth2ErrorCodes.InvalidCredentials);
            return Page();
        }

        // Cannot remove the last project-admin
        if (User.IsInRole("ProjectAdmin"))
        {
            Fail(Auth2ErrorCodes.Forbidden);
            return Page();
        }

        var adminUserSvc = Svc<IAdminUserService>();
        await adminUserSvc.RemoveAsync(CurrentUser!.Id, HttpContext.RequestAborted);

        await HttpContext.SignOutAsync(Auth2Cookie.Scheme);
        return Redirect("/");
    }
}
