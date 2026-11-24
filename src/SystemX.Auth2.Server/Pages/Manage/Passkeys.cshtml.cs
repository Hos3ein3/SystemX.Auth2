#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Manage;

/// <summary>
/// The passkeys of the signed-in user. The list, rename and delete work as page handlers; registering needs the browser script and uses
/// <c>POST /account/passkey/creation-options</c> and <c>/account/passkey/register</c> (see <c>docs/ui-handover.md</c>).
/// </summary>
public sealed class PasskeysModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text, IPasskeyService passkeys)
    : ManagePageModel(clientId, flow, urls, text)
{
    /// <summary>The passkeys of the user.</summary>
    public IReadOnlyList<PasskeyDto> Passkeys { get; private set; } = [];
    /// <summary>Passkeys are allowed in this application.</summary>
    public bool PasskeysAllowed { get; private set; }
    /// <summary>The user may register more passkeys.</summary>
    public bool CanAddMore { get; private set; }
    /// <summary>True when passkeys exist and none of them is backed up: the UI should suggest adding another one.</summary>
    public bool SuggestBackupPasskey { get; private set; }
    [BindProperty] public string? Id { get; set; }
    [BindProperty] public string? Name { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRenameAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        var result = await passkeys.RenameAsync(CurrentUser!, Id ?? "", Name ?? "", HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result.ErrorCode!); await LoadAsync(); return Page(); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        var result = await passkeys.RemoveAsync(CurrentUser!, Id ?? "", HttpContext.RequestAborted);
        if (!result.Succeeded) { Fail(result.ErrorCode!); await LoadAsync(); return Page(); }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Passkeys = await passkeys.ListAsync(CurrentUser!, HttpContext.RequestAborted);
        PasskeysAllowed = Settings.Passkeys.AllowPasskeys;
        CanAddMore = PasskeysAllowed && Passkeys.Count < Settings.Passkeys.MaxPasskeysPerUser;
        SuggestBackupPasskey = Passkeys.Count > 0 && Passkeys.All(p => !p.IsBackedUp);
    }
}
