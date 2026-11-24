using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Samples.Mvc.Models;

namespace SystemX.Auth2.Samples.Mvc.Controllers;

/// <summary>
/// MVC Authentication controller: handles sign-in challenge, sign-out, profile display, and access denied views.
/// </summary>
public class AccountController(ICurrentUser currentUser, ICurrentClient currentClient) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        var redirectUri = SafeReturnUrl(returnUrl);
        return Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, [Auth2AuthSchemes.Oidc]);
    }

    [HttpGet]
    [HttpPost]
    [Authorize]
    public IActionResult Logout(string? returnUrl = null)
    {
        var redirectUri = SafeReturnUrl(returnUrl);
        return SignOut(
            new AuthenticationProperties { RedirectUri = redirectUri },
            [Auth2AuthSchemes.Cookie, Auth2AuthSchemes.Oidc]);
    }

    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var model = new UserProfileViewModel
        {
            IsAuthenticated = currentUser.IsAuthenticated,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Email = currentUser.Email,
            PhoneNumber = currentUser.PhoneNumber,
            FirstName = currentUser.FirstName,
            LastName = currentUser.LastName,
            FullName = currentUser.FullName,
            Roles = await currentUser.GetRolesAsync(ct),
            Permissions = await currentUser.GetPermissionsAsync(ct),
            Groups = await currentUser.GetGroupsAsync(ct),
            ClientId = currentClient.ClientId,
            ProjectKey = currentClient.ProjectKey,
            Scopes = currentClient.Scopes,
            IsMachine = currentClient.IsMachine,
            IdentityProvider = currentClient.Idp,
            Amr = currentClient.Amr,
            SessionId = currentClient.SessionId
        };

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
