#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Identity;
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
        ClientId = User.FindFirst(Auth2Claims.ClientId)?.Value;
        ReturnUrl = null;
        if (!await LoadProjectAsync()) return false;

        var user = await Svc<UserManager<AppUser>>().FindByIdAsync(User.FindFirst("sub")?.Value ?? "");
        if (User.FindFirst(Auth2Claims.ProjectId)?.Value != Project!.Id.ToString("D")
            || user is null || !await Svc<ILoginService>().IsSubjectValidAsync(user, HttpContext.RequestAborted))
        {
            Fail(Auth2ErrorCodes.InvalidToken, StatusCodes.Status401Unauthorized);
            return false;
        }
        CurrentUser = user;
        return true;
    }
}
