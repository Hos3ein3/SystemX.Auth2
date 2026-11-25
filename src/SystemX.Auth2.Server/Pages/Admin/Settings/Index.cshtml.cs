using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Pages.Admin.Settings;

public sealed class IndexModel : AdminPageModel
{
    public ProjectSettings CurrentSettings { get; private set; } = new();
    public int SettingsVersion { get; private set; }

    [BindProperty] public ProjectSettings InputSettings { get; set; } = new();
    [BindProperty] public int FormVersion { get; set; }

    // List & Multi-line Bindings
    [BindProperty] public List<string> SelectedLoginMethods { get; set; } = [];
    [BindProperty] public List<string> SelectedRegisterMethods { get; set; } = [];
    [BindProperty] public List<string> SelectedClientTypes { get; set; } = [];
    [BindProperty] public string? BannedPasswordsText { get; set; }
    [BindProperty] public string? AllowedEmailDomainsText { get; set; }
    [BindProperty] public string? BlockedEmailDomainsText { get; set; }
    [BindProperty] public string? SupportedCulturesText { get; set; }

    // Registration Defaults Multi-select Bindings
    [BindProperty] public List<string> SelectedAssignedRoles { get; set; } = [];
    [BindProperty] public List<string> SelectedAssignedGroups { get; set; } = [];
    [BindProperty] public List<string> SelectedAssignedPermissions { get; set; } = [];

    public IReadOnlyList<string> AvailableRoles { get; private set; } = [];
    public IReadOnlyList<string> AvailableGroups { get; private set; } = [];
    public IReadOnlyList<string> AvailablePermissions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var projectSvc = Svc<IAdminProjectService>();
        var settingsTuple = await projectSvc.GetSettingsAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        if (settingsTuple is not null)
        {
            CurrentSettings = settingsTuple.Value.Settings;
            SettingsVersion = settingsTuple.Value.Version;
            InputSettings = CurrentSettings;
            FormVersion = SettingsVersion;

            SelectedLoginMethods = CurrentSettings.Methods.AllowedLoginMethods.Select(m => m.ToString()).ToList();
            SelectedRegisterMethods = CurrentSettings.Methods.AllowedRegisterMethods.Select(m => m.ToString()).ToList();
            SelectedClientTypes = CurrentSettings.Clients.AllowedClientTypes.Select(t => t.ToString()).ToList();
            BannedPasswordsText = string.Join("\n", CurrentSettings.Password.BannedPasswords);
            AllowedEmailDomainsText = string.Join("\n", CurrentSettings.Email.AllowedDomains);
            BlockedEmailDomainsText = string.Join("\n", CurrentSettings.Email.BlockedDomains);
            SupportedCulturesText = string.Join(", ", CurrentSettings.Localization.SupportedCultures);

            SelectedAssignedRoles = [.. CurrentSettings.Registration.AssignedRoles];
            SelectedAssignedGroups = [.. CurrentSettings.Registration.AssignedGroups];
            SelectedAssignedPermissions = [.. CurrentSettings.Registration.AssignedPermissions];
        }

        await LoadAvailableOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var projectSvc = Svc<IAdminProjectService>();
        var settingsTuple = await projectSvc.GetSettingsAsync(CurrentProject!.Id, HttpContext.RequestAborted);
        if (settingsTuple is not null)
        {
            CurrentSettings = settingsTuple.Value.Settings;
        }

        // Map list bindings
        InputSettings.Methods.AllowedLoginMethods = ParseEnums<LoginMethod>(SelectedLoginMethods);
        InputSettings.Methods.AllowedRegisterMethods = ParseEnums<LoginMethod>(SelectedRegisterMethods);
        InputSettings.Clients.AllowedClientTypes = ParseEnums<ClientType>(SelectedClientTypes);
        InputSettings.Password.BannedPasswords = ParseLines(BannedPasswordsText);
        InputSettings.Email.AllowedDomains = ParseLines(AllowedEmailDomainsText);
        InputSettings.Email.BlockedDomains = ParseLines(BlockedEmailDomainsText);
        var cultures = ParseLines(SupportedCulturesText);
        if (cultures.Count == 0 && !string.IsNullOrWhiteSpace(InputSettings.Localization.DefaultCulture))
        {
            cultures.Add(InputSettings.Localization.DefaultCulture);
        }
        InputSettings.Localization.SupportedCultures = cultures;

        if (IsGlobalAdmin)
        {
            InputSettings.Registration.AssignedRoles = [.. SelectedAssignedRoles.Distinct(StringComparer.OrdinalIgnoreCase)];
            InputSettings.Registration.AssignedGroups = [.. SelectedAssignedGroups.Distinct(StringComparer.OrdinalIgnoreCase)];
            InputSettings.Registration.AssignedPermissions = [.. SelectedAssignedPermissions.Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        else
        {
            // Preserve strictly GlobalOnly settings
            InputSettings.SchemaVersion = CurrentSettings.SchemaVersion;
            InputSettings.CredentialScope = CurrentSettings.CredentialScope;
            InputSettings.Registration.AssignedRoles = CurrentSettings.Registration.AssignedRoles;
            InputSettings.Registration.AssignedGroups = CurrentSettings.Registration.AssignedGroups;
            InputSettings.Registration.AssignedPermissions = CurrentSettings.Registration.AssignedPermissions;
            InputSettings.Password = CurrentSettings.Password;
        }

        var (result, newVersion) = await projectSvc.UpdateSettingsAsync(
            CurrentProject!.Id,
            InputSettings,
            IsGlobalAdmin,
            FormVersion,
            HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            var msg = result.Errors?.Count > 0
                ? string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
                : (result.ErrorCode ?? "Failed to save project settings.");
            TempData["Error"] = msg;
            await LoadAvailableOptionsAsync();
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        TempData["Success"] = "Project settings saved successfully.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    private async Task LoadAvailableOptionsAsync()
    {
        var db = Svc<Auth2DbContext>();
        var projectId = CurrentProject!.Id;
        var reserved = ProjectSettingsValidator.ReservedRoles;

        var roles = await db.Roles.AsNoTracking()
            .Where(r => r.ProjectId == projectId || (r.ProjectId == null && CurrentProject.IsGlobalProject))
            .OrderBy(r => r.Name)
            .Select(r => r.Name!)
            .ToListAsync(HttpContext.RequestAborted);

        AvailableRoles = [.. roles.Where(r => !reserved.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Union(CurrentSettings.Registration.AssignedRoles)
            .Order(StringComparer.OrdinalIgnoreCase)];

        var groups = await db.Groups.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Name)
            .Select(g => g.Name)
            .ToListAsync(HttpContext.RequestAborted);

        AvailableGroups = [.. groups.Union(CurrentSettings.Registration.AssignedGroups).Order(StringComparer.OrdinalIgnoreCase)];

        var globalProj = await db.Projects.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectKey == ProjectKeyRules.GlobalProjectKey, HttpContext.RequestAborted);
        var globalId = globalProj?.Id;

        var perms = await db.Permissions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => (p.ProjectId == projectId || (globalId != null && p.ProjectId == globalId.Value)) && !p.IsObsolete)
            .OrderBy(p => p.Key)
            .Select(p => p.Key)
            .ToListAsync(HttpContext.RequestAborted);

        AvailablePermissions = [.. perms.Union(CurrentSettings.Registration.AssignedPermissions).Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static List<T> ParseEnums<T>(IEnumerable<string> strings) where T : struct, Enum
    {
        var list = new List<T>();
        foreach (var s in strings)
        {
            if (Enum.TryParse<T>(s, true, out var val))
            {
                list.Add(val);
            }
        }
        return list;
    }

    private static List<string> ParseLines(string? text) =>
        (text ?? "")
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
