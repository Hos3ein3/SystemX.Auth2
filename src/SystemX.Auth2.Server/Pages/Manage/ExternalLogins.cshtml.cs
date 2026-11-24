#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Pages.Manage;

/// <summary>The external logins of the signed-in user: list, link a provider, unlink one.</summary>
public sealed class ExternalLoginsModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text,
    IExternalLinkService links, ExternalFlow external) : ManagePageModel(clientId, flow, urls, text)
{
    /// <summary>The links of the user.</summary>
    public IReadOnlyList<ExternalLinkInfo> Links { get; private set; } = [];
    /// <summary>Providers of the application the user can still link.</summary>
    public IReadOnlyList<ExternalProviderInfoDto> AvailableProviders { get; private set; } = [];
    /// <summary>SAML POST binding: the address of the form the UI must post.</summary>
    public string? PostAction { get; private set; }
    public IReadOnlyDictionary<string, string>? PostFields { get; private set; }
    [BindProperty] public string? Provider { get; set; }
    [BindProperty] public string? ProviderKey { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        await LoadAsync();
        return Page();
    }

    /// <summary>Starts linking a provider to the account.</summary>
    public async Task<IActionResult> OnPostLinkAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        var start = await external.StartAsync(HttpContext, Provider ?? "", "/Manage/ExternalLogins", null, CurrentUser!.Id.ToString("D"), HttpContext.RequestAborted);
        if (!start.Succeeded) { Fail(start.ErrorCode ?? Auth2ErrorCodes.ExternalProviderDisabled); await LoadAsync(); return Page(); }
        if (start.PostFields is not null) { PostAction = start.RedirectTo; PostFields = start.PostFields; await LoadAsync(); return Page(); }
        return Redirect(start.RedirectTo!);
    }

    /// <summary>Removes a link. The last way to sign in cannot be removed.</summary>
    public async Task<IActionResult> OnPostUnlinkAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        var result = await links.UnlinkAsync(CurrentUser!, Provider ?? "", ProviderKey ?? "", HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result.ErrorCode!); await LoadAsync(); return Page(); }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Links = await links.ListAsync(CurrentUser!, HttpContext.RequestAborted);
        var linked = Links.Select(l => l.Provider).ToHashSet();
        AvailableProviders = [.. (await external.ListAsync(HttpContext.RequestAborted)).Where(p => !p.LoginFormRequired && !linked.Contains(p.Name))];
    }
}
