#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Pages.Manage;

/// <summary>
/// Base of the self-service pages. They need the hosted-UI session cookie. The application comes from the cookie
/// (claim <c>client_id</c>), and the cookie must belong to that application.
/// </summary>
public abstract class ManagePageModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{
    /// <summary>The signed-in user.</summary>
    public AppUser? CurrentUser { get; private set; }

    /// <summary>Loads the application, the user and the settings from the session cookie. False means: stop, the status code is set.</summary>
    protected async Task<bool> LoadSessionAsync()
    {
        var queryCid = Request.Query["ClientId"].ToString();
        if (string.IsNullOrWhiteSpace(queryCid)) queryCid = Request.Query["client_id"].ToString();
        var cookieCid = User.FindFirst(Auth2Claims.ClientId)?.Value;
        ClientId = !string.IsNullOrWhiteSpace(queryCid) ? queryCid : cookieCid;
        ReturnUrl = null;
        if (!await LoadProjectAsync()) return false;

        var isGlobalAdmin = User.IsInRole("GlobalAdmin") || User.IsInRole("Developer") || User.HasClaim("scope", "auth2.admin.global");
        var user = await Svc<UserManager<AppUser>>().FindByIdAsync(User.FindFirst("sub")?.Value ?? "");
        if ((!isGlobalAdmin && User.FindFirst(Auth2Claims.ProjectId)?.Value != Project!.Id.ToString("D"))
            || user is null || !await Svc<ILoginService>().IsSubjectValidAsync(user, HttpContext.RequestAborted))
        {
            Fail(Auth2ErrorCodes.InvalidToken, StatusCodes.Status401Unauthorized);
            return false;
        }
        CurrentUser = user;
        return true;
    }

    /// <summary>
    /// For the restricted pages (deleting the account, turning two-factor off): when the person has two-factor on and has not given a code of the authenticator app within
    /// <c>TwoFactor.StepUpMinutes</c>, this is the redirect to <c>/Account/VerifyTotp</c> that comes back to <paramref name="returnTo"/> (a local path; this request when null).
    /// Null means: go on (no two-factor, the setting is 0, or the code is fresh). Call it after <see cref="LoadSessionAsync"/>.
    /// </summary>
    protected async Task<IActionResult?> RequireRecentTotpAsync(string? returnTo = null)
    {
        var t = Settings.TwoFactor;
        if (CurrentUser is null || !t.AllowTotp || t.StepUpMinutes <= 0) return null;
        if (!await Svc<UserManager<AppUser>>().GetTwoFactorEnabledAsync(CurrentUser)) return null;   // no authenticator app: there is no code to ask for
        if (Infrastructure.Auth.StepUp.IsFresh(User, t.StepUpMinutes, Svc<TimeProvider>().GetUtcNow())) return null;

        var back = returnTo ?? Request.Path.Value + Request.QueryString.Value;
        return Redirect($"/Account/VerifyTotp?ClientId={Uri.EscapeDataString(ClientId ?? "")}&ReturnUrl={Uri.EscapeDataString(back)}");
    }

    /// <summary>
    /// Signs the same session in again (so the cookie shows new data), keeping the time of the last code. <paramref name="codeGivenNow"/> sets it to now (a code was just accepted).
    /// </summary>
    protected Task RefreshSessionAsync(AppUser user, bool codeGivenNow = false)
    {
        var now = Svc<TimeProvider>().GetUtcNow();
        return Infrastructure.Auth.Auth2Cookie.SignInAsync(HttpContext, user, Project!, Client, Effective, User.FindAll(Auth2Claims.Amr).Select(c => c.Value),
            User.FindFirst(Auth2Claims.SessionId)?.Value, User.FindFirst(Auth2Claims.IdentityProvider)?.Value,
            stepUpAtUtc: codeGivenNow ? now : Infrastructure.Auth.StepUp.LastAt(User));
    }
}
