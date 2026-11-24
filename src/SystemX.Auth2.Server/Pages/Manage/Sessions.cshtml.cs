#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Pages.Manage;
namespace SystemX.Auth2.Server.Pages.Manage;

public sealed class SessionsModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{

    public IReadOnlyList<SessionInfo> Sessions { get; private set; } = [];
    [BindProperty] public string? AuthorizationId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        Sessions = await Svc<ISessionService>().ListAsync(CurrentUser!, Project!, HttpContext.RequestAborted);
        return Page();
    }

    /// <summary>Ends one session (the tokens of one sign-in).</summary>
    public async Task<IActionResult> OnPostRevokeAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        if (string.IsNullOrEmpty(AuthorizationId) || !await Svc<ISessionService>().RevokeAsync(CurrentUser!, Project!, AuthorizationId, HttpContext.RequestAborted))
        {
            Fail(Auth2ErrorCodes.NotFound, StatusCodes.Status404NotFound);
            return Page();
        }
        return RedirectToPage();
    }
}
