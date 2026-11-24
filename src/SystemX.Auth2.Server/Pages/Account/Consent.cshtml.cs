#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Pages.Account;

[Authorize(AuthenticationSchemes = Auth2Cookie.Scheme)]
public sealed class ConsentModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : Auth2PageModel(clientId, flow, urls, text)
{

    /// <summary>The scopes the application asks for (from the authorize URL in <c>ReturnUrl</c>).</summary>
    public IReadOnlyList<string> Scopes { get; private set; } = [];
    public string? ApplicationName => BrandName;
    public string? RedirectUri { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync()) return Page();
        return Page();
    }

    /// <summary>The user agrees. We go back to the authorize URL with a short-lived consent token.</summary>
    public async Task<IActionResult> OnPostAcceptAsync()
    {
        if (!await PrepareAsync()) return Page();
        var user = await Svc<Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.AppUser>>().FindByIdAsync(User.FindFirst("sub")!.Value);
        var token = Svc<IConsentService>().CreateGrantToken(user!, Client!, Scopes);
        return Redirect(ReturnUrl + "&consent=" + Uri.EscapeDataString(token));
    }

    /// <summary>The user refuses. The application gets <c>error=access_denied</c> (only if its redirect URI is registered).</summary>
    public async Task<IActionResult> OnPostDenyAsync()
    {
        if (!await PrepareAsync()) return Page();
        if (RedirectUri is null) return Redirect("/");
        var state = ReturnUrlQuery.Get(ReturnUrl, "state");
        var sep = RedirectUri.Contains('?') ? "&" : "?";
        return Redirect($"{RedirectUri}{sep}error=access_denied" + (state is null ? "" : "&state=" + Uri.EscapeDataString(state)));
    }

    private async Task<bool> PrepareAsync()
    {
        if (!await LoadProjectAsync()) return false;
        // The cookie must be the sign-in for this application, and ReturnUrl must be an authorize request of ours.
        if (User.FindFirst(Auth2Claims.ProjectId)?.Value != Project!.Id.ToString("D")
            || ReturnUrl is null || !ReturnUrl.StartsWith("/connect/authorize?", StringComparison.Ordinal) || !Svc<IReturnUrlValidator>().IsAllowed(ReturnUrl, []))
        {
            Fail(Auth2ErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest);
            return false;
        }
        Scopes = (ReturnUrlQuery.Get(ReturnUrl, "scope") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var redirect = ReturnUrlQuery.Get(ReturnUrl, "redirect_uri");
        RedirectUri = Svc<IReturnUrlValidator>().IsAllowed(redirect, Effective.AllowedRedirectUris) ? redirect : null;
        return true;
    }
}
