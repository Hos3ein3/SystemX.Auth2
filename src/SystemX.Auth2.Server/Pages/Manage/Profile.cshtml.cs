using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Pages.Manage;

public sealed class ProfileModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : ManagePageModel(clientId, flow, urls, text)
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool Saved { get; private set; }

    public sealed class InputModel
    {
        [StringLength(100)] public string? FirstName { get; set; }
        [StringLength(100)] public string? LastName { get; set; }
        [StringLength(200)] public string? DisplayName { get; set; }
        [StringLength(10)] public string? PreferredCulture { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        var u = CurrentUser!;
        Input = new InputModel
        {
            FirstName = u.FirstName,
            LastName = u.LastName,
            DisplayName = u.DisplayName,
            PreferredCulture = u.PreferredCulture
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadSessionAsync()) return Page();
        if (!ModelState.IsValid) { Fail(Auth2ErrorCodes.ValidationFailed); return Page(); }

        var userManager = Svc<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(CurrentUser!.Id.ToString("D"));
        if (user is null) { Fail(Auth2ErrorCodes.NotFound); return Page(); }

        user.FirstName = Input.FirstName;
        user.LastName = Input.LastName;
        user.DisplayName = Input.DisplayName;
        user.PreferredCulture = Input.PreferredCulture;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            Fail(Auth2ErrorCodes.ValidationFailed);
            return Page();
        }

        Saved = true;
        return Page();
    }
}
