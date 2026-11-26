#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Pages.Manage;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>
/// A signed-in person proves, right now, that they hold the authenticator app: they type the 6-digit code it shows. The Server checks it with
/// <see cref="ITwoFactorService.VerifyAsync"/>: a code works <b>once</b>, a wrong code counts as a failed sign-in attempt (guesses lock the account), the answer is
/// audited (<c>2fa.step_up</c>, <c>2fa.step_up_failed</c>, <c>2fa.step_up_replayed</c>) and the page is rate limited like the other code pages.
/// With a valid <c>ReturnUrl</c> (one the application allows) the person goes there afterwards; without one the page says the code was accepted.
/// This is the hosted page for the same check that <c>WithTotp()</c> / <c>[Auth2Totp]</c> asks for in the Client package.
/// </summary>
public sealed class VerifyTotpModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{
    [BindProperty] public InputModel Input { get; set; } = new();

    /// <summary>The project allows authenticator-app codes (<c>TwoFactor.AllowTotp</c>).</summary>
    public bool TotpAllowed { get; private set; }

    /// <summary>The person has turned on two-factor, so they have a code to give.</summary>
    public bool TwoFactorEnabled { get; private set; }

    /// <summary>True after a right code (when there is no page to go on to).</summary>
    public bool Verified { get; private set; }

    public sealed class InputModel
    {
        /// <summary>The code of the authenticator app. Spaces are ignored.</summary>
        [Required, StringLength(16)] public string Code { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return Page();
        if (!TotpAllowed) { Fail(Auth2ErrorCodes.TwoFactorNotAllowed); return Page(); }
        if (!TwoFactorEnabled) { Fail(Auth2ErrorCodes.TwoFactorNotEnabled); return Page(); }
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        var result = await Svc<ITwoFactorService>().VerifyAsync(CurrentUser!, Input.Code, ct: HttpContext.RequestAborted);
        Input.Code = "";   // never sent back to the page
        if (!result.Succeeded)
        {
            Fail(result, Endpoints.ApiResults.StatusFor(result.ErrorCode ?? Auth2ErrorCodes.ValidationFailed));
            return Page();
        }

        await RefreshSessionAsync(CurrentUser!, codeGivenNow: true);   // the restricted pages do not ask again for TwoFactor.StepUpMinutes
        if (SafeReturnUrl() is { } next) return Redirect(next);
        Verified = true;
        return Page();
    }

    /// <summary>
    /// Where the "back" link goes. When the page was opened by an application that asked for a step-up (the return address is its authorize request with
    /// <see cref="Auth2AcrValues.Totp"/>), the way back is the same request <b>without</b> that value: going to the same request again would only ask for the code again.
    /// The application then gets its sign-in, and decides by itself (a request that needs the code is refused there).
    /// </summary>
    public string BackUrl()
    {
        var next = SafeReturnUrl() ?? "/";
        var mark = next.IndexOf('?');
        if (mark < 0 || !next.StartsWith("/connect/authorize", StringComparison.OrdinalIgnoreCase)) return next;
        var kept = new List<KeyValuePair<string, string?>>();
        foreach (var (key, values) in Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(next[mark..]))
            foreach (var value in values)
            {
                if (!key.Equals("acr_values", StringComparison.Ordinal)) { kept.Add(new(key, value)); continue; }
                var rest = string.Join(' ', (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(v => v != Auth2AcrValues.Totp));
                if (rest.Length > 0) kept.Add(new(key, rest));   // other values of the application stay
            }
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(next[..mark], kept);
    }

    private async Task<bool> LoadAsync()
    {
        var returnUrl = ReturnUrl;   // loading the session clears it (the session knows the application): it is checked and used here
        if (!await LoadSessionAsync()) return false;
        ReturnUrl = returnUrl;
        TotpAllowed = Settings.TwoFactor.AllowTotp;
        TwoFactorEnabled = await Svc<UserManager<AppUser>>().GetTwoFactorEnabledAsync(CurrentUser!);
        return true;
    }
}
