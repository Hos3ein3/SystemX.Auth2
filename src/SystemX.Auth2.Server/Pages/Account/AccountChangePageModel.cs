#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Pages.Manage;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>
/// Base of the pages where a signed-in person changes their own password, user name, email address or phone number.
/// The person comes from the hosted-UI session cookie (see <see cref="ManagePageModel"/>). A person who has a password must give it again
/// for every change except the password itself (which asks for the current one anyway); <see cref="IAccountChangeService"/> checks it and does the change.
/// </summary>
public abstract class AccountChangePageModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{
    /// <summary>True when the person has a password, so the page needs <c>Input.CurrentPassword</c>. A person who only signs in with an external account or a passkey has none.</summary>
    public bool PasswordRequired { get; private set; }

    /// <summary>Loads the session and sets <see cref="PasswordRequired"/>. False means: stop, the status code is set.</summary>
    protected async Task<bool> LoadAsync()
    {
        if (!await LoadSessionAsync()) return false;
        PasswordRequired = await Svc<UserManager<AppUser>>().HasPasswordAsync(CurrentUser!);
        return true;
    }

    /// <summary>Shows the failure of <see cref="IAccountChangeService"/> or <see cref="IPasswordService"/> with the status code the API uses for the same code (400, 423 locked, 429 too soon).</summary>
    protected void FailWith(OperationResult result) => Fail(result, Endpoints.ApiResults.StatusFor(result.ErrorCode ?? Auth2ErrorCodes.ValidationFailed));

    /// <summary>Signs the same session in again, so the cookie shows the new user name.</summary>
    protected Task RefreshCookieAsync(AppUser user) => RefreshSessionAsync(user);   // keeps the time of the last code of the authenticator app

    /// <summary>Removes the hosted-UI session cookie.</summary>
    protected Task SignOutCookieAsync() => HttpContext.SignOutAsync(Auth2Cookie.Scheme);
}
