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
/// for every change except the password itself (which asks for the current one anyway); a wrong password counts as a failed attempt.
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

    /// <summary>
    /// Checks the password that confirms a change. True when there is nothing to check (the person has no password) or it is right.
    /// Otherwise the page error is set and a wrong password counts as a failed attempt, so guessing it locks the account like a sign-in does.
    /// </summary>
    protected async Task<bool> ConfirmWithPasswordAsync(string? password)
    {
        if (!PasswordRequired) return true;
        var users = Svc<UserManager<AppUser>>();
        if (await users.IsLockedOutAsync(CurrentUser!)) { Fail(Auth2ErrorCodes.LockedOut, StatusCodes.Status403Forbidden); return false; }
        if (string.IsNullOrEmpty(password)) { Fail(Auth2ErrorCodes.ValidationFailed); return false; }
        if (await users.CheckPasswordAsync(CurrentUser!, password)) return true;
        await users.AccessFailedAsync(CurrentUser!);
        Fail(Auth2ErrorCodes.InvalidCredentials);
        return false;
    }

    /// <summary>
    /// Runs the user validators (format, rules of the project, uniqueness) on a copy of the person with the new values, <b>before</b> anything is sent or saved.
    /// True when the values are fine; otherwise the page error lists what is wrong.
    /// </summary>
    protected async Task<bool> ValidateChangeAsync(string? userName = null, string? email = null, string? phoneNumber = null)
    {
        var users = Svc<UserManager<AppUser>>();
        var probe = new AppUser { Id = CurrentUser!.Id, UserName = userName ?? CurrentUser.UserName, Email = email ?? CurrentUser.Email, PhoneNumber = phoneNumber ?? CurrentUser.PhoneNumber };
        var errors = new List<IdentityError>();
        foreach (var validator in users.UserValidators)
        {
            var result = await validator.ValidateAsync(users, probe);
            if (!result.Succeeded) errors.AddRange(result.Errors);
        }
        if (errors.Count == 0) return true;
        Fail(OperationResult.Fail(errors));
        return false;
    }

    /// <summary>Writes the audit event of a change of the own account.</summary>
    protected Task AuditAsync(string action) =>
        Svc<IAuditService>().WriteAsync(new AuditEntry(action, Project!.Id, ActorUserId: CurrentUser!.Id, TargetType: "user", TargetId: CurrentUser.Id.ToString()), HttpContext.RequestAborted);

    /// <summary>Signs the same session in again, so the cookie shows the new user name.</summary>
    protected Task RefreshCookieAsync(AppUser user) =>
        Auth2Cookie.SignInAsync(HttpContext, user, Project!, Client, Effective, User.FindAll(Auth2Claims.Amr).Select(c => c.Value),
            User.FindFirst(Auth2Claims.SessionId)?.Value, User.FindFirst(Auth2Claims.IdentityProvider)?.Value);

    /// <summary>Removes the hosted-UI session cookie.</summary>
    protected Task SignOutCookieAsync() => HttpContext.SignOutAsync(Auth2Cookie.Scheme);
}
