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

public sealed class IndexModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{

    public string? UserName { get; private set; }
    public string? Email { get; private set; }
    public bool EmailConfirmed { get; private set; }
    public string? PhoneNumber { get; private set; }
    public bool PhoneConfirmed { get; private set; }
    public bool TwoFactorEnabled { get; private set; }
    public bool ShowTwoFactor { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        var u = CurrentUser!;
        (UserName, Email, EmailConfirmed, PhoneNumber, PhoneConfirmed) = (u.UserName, u.Email, u.EmailConfirmed, u.PhoneNumber, u.PhoneNumberConfirmed);
        ShowTwoFactor = Settings.TwoFactor.Mode != Domain.Settings.TwoFactorMode.Off && Settings.TwoFactor.AllowTotp;
        TwoFactorEnabled = await Svc<Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.AppUser>>().GetTwoFactorEnabledAsync(u);
        return Page();
    }
}
