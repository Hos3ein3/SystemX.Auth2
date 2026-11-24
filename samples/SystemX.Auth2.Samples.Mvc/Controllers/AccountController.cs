using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Contracts.Auth;
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
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturnUrl(returnUrl));
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    /// <summary>Self UI: this app collects the password and sends it to the Auth2 server; the package signs the cookie in.</summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, [FromServices] IAuth2WebService web, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await web.LoginAsync(HttpContext, new LoginRequest(model.Login, model.Password), model.RememberMe, ct);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError("", result.Error!.Code is "invalid_credentials" ? "The user name or password is wrong." : $"Sign-in failed ({result.Error.Code}).");
            return View(model);
        }
        if (result.Value!.TwoFactorRequired)
        {
            ModelState.AddModelError("", "This account needs a two-factor code. The sample does not show that step; use the hosted UI (.UseHostedUi()) for it.");
            return View(model);
        }
        return LocalRedirect(SafeReturnUrl(model.ReturnUrl));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout([FromServices] IAuth2WebService web, string? returnUrl = null, CancellationToken ct = default)
    {
        await web.LogoutAsync(HttpContext, ct);
        return LocalRedirect(SafeReturnUrl(returnUrl));
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
