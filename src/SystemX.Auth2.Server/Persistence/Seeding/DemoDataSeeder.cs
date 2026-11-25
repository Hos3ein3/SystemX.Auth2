using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Persistence.Seeding;

/// <summary>
/// Demo people and access data for the <b>sample project</b>, so the sample applications (MVC and Minimal API) can be tried at once and the admin
/// screens have something to show: <b>60 permissions, 55 roles, 53 groups, 60 scopes and 56 users</b>, and who has what.
/// Safe to run many times (it only adds what is missing; a finished seed is recognised and skipped quickly).
/// <see cref="Auth2Seeder"/> calls it wherever it adds the sample project, unless <c>Auth2:SeedDemoData</c> is <c>false</c>.
/// Every demo user has the public password <see cref="Password"/>. See <c>docs/seeding.md</c>.
/// </summary>
public sealed class DemoDataSeeder(IServiceScopeFactory scopes)
{
    /// <summary>The password of every demo user. It is public: never use this seed on a real server.</summary>
    public const string Password = "Demo@12345!";

    /// <summary>The key of the project that gets the demo data.</summary>
    public const string ProjectKey = "sample-project";

    // ---------------------------------------------------------------- permissions: 12 areas x 5 actions = 60

    internal static readonly string[] Areas = ["orders", "invoices", "customers", "products", "inventory", "shipping", "payments", "reports", "users", "settings", "audit", "support"];
    internal static readonly string[] Actions = ["read", "write", "delete", "export", "approve"];
    private static readonly string[] ActionVerbs = ["Read", "Write", "Delete", "Export", "Approve"];

    /// <summary>Every permission key: <c>orders.read</c>, <c>orders.write</c>, ... (12 areas, 5 actions).</summary>
    public static IReadOnlyList<string> PermissionKeys { get; } = [.. Areas.SelectMany(a => Actions.Select(x => $"{a}.{x}"))];

    private static string Title(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    internal static readonly (string Key, string Name, string Description, string Group)[] Permissions =
    [
        .. Areas.SelectMany(area => Actions.Select((action, i) =>
            ($"{area}.{action}", $"{ActionVerbs[i]} {Title(area)}", $"{ActionVerbs[i]} {area} in the sample project.", area))),
    ];

    // ---------------------------------------------------------------- scopes: api.<area>.<action> = 60

    /// <summary>Every custom scope: <c>api.orders.read</c>, ... They are allowed for both sample applications.</summary>
    public static IReadOnlyList<string> ScopeNames { get; } = [.. Areas.SelectMany(a => Actions.Select(x => $"api.{a}.{x}"))];

    // ---------------------------------------------------------------- roles: 5 base + 36 per area + 14 jobs = 55

    private static readonly string[] AreaRoleLevels = ["Viewer", "Editor", "Manager"];

    internal static readonly (string Name, string Description, string[] Permissions)[] Roles =
    [
        ("Admin", "Administrator of the sample project: every permission.", [.. PermissionKeys]),
        ("Manager", "Manages orders and reads reports.", ["reports.read", "orders.read", "orders.write", "users.read"]),
        ("Editor", "Creates and changes orders.", ["orders.read", "orders.write"]),
        ("Viewer", "Reads orders.", ["orders.read"]),
        ("Auditor", "Reads reports.", ["reports.read"]),

        // OrdersViewer, OrdersEditor, OrdersManager, InvoicesViewer, ... (36)
        .. Areas.SelectMany(area => AreaRoleLevels.Select(level => (
            Name: Title(area) + level,
            Description: level switch { "Viewer" => $"Reads {area}.", "Editor" => $"Reads and changes {area}.", _ => $"Full control of {area}." },
            Permissions: level switch
            {
                "Viewer" => new[] { $"{area}.read" },
                "Editor" => [$"{area}.read", $"{area}.write"],
                _ => Actions.Select(a => $"{area}.{a}").ToArray(),
            }))),

        ("BillingClerk", "Prepares invoices.", ["invoices.read", "invoices.write", "payments.read"]),
        ("WarehouseOperator", "Works in the warehouse.", ["inventory.read", "inventory.write", "shipping.read"]),
        ("ShippingCoordinator", "Plans shipments.", ["shipping.read", "shipping.write", "orders.read"]),
        ("SupportAgent", "Answers customers.", ["support.read", "support.write", "customers.read", "orders.read"]),
        ("SupportLead", "Leads the support team.", ["support.read", "support.write", "support.approve", "customers.read", "users.read"]),
        ("DataAnalyst", "Reads and exports reports.", ["reports.read", "reports.export", "orders.read", "invoices.read"]),
        ("ComplianceOfficer", "Checks the audit trail.", ["audit.read", "audit.export", "reports.read", "users.read"]),
        ("SecurityOfficer", "Looks after accounts and settings.", ["audit.read", "users.read", "users.write", "settings.read"]),
        ("OperationsLead", "Approves orders and watches the flow.", ["orders.read", "orders.approve", "inventory.read", "shipping.read", "reports.read"]),
        ("FinanceController", "Approves invoices and payments.", ["invoices.read", "invoices.approve", "payments.read", "payments.approve", "reports.read", "reports.export"]),
        ("SalesRepresentative", "Sells.", ["orders.read", "orders.write", "customers.read", "products.read"]),
        ("SalesLead", "Leads the sales team.", ["orders.read", "orders.write", "orders.approve", "customers.read", "customers.write", "products.read", "reports.read"]),
        ("CustomerSuccess", "Looks after customers.", ["customers.read", "customers.write", "support.read", "orders.read"]),
        ("ContentEditor", "Edits the product pages.", ["products.read", "products.write"]),
    ];

    /// <summary>The names of all demo roles.</summary>
    public static IReadOnlyList<string> RoleNames { get; } = [.. Roles.Select(r => r.Name)];

    // ---------------------------------------------------------------- groups: 3 teams + 10 departments x 5 regions = 53

    private static readonly string[] Departments = ["finance", "sales", "support", "warehouse", "logistics", "marketing", "legal", "hr", "it", "product"];
    private static readonly string[] DepartmentRoles = ["FinanceController", "SalesRepresentative", "SupportAgent", "WarehouseOperator", "ShippingCoordinator", "ContentEditor", "ComplianceOfficer", "UsersViewer", "SecurityOfficer", "ProductsManager"];
    private static readonly string[] Regions = ["eu", "us", "asia", "africa", "latam"];

    internal static readonly (string Name, string DisplayName, string[] Roles, string[] Permissions)[] Groups =
    [
        ("finance", "Finance team", ["Auditor"], ["orders.read"]),
        ("sales", "Sales team", ["Editor"], []),
        ("support", "Support team", ["Viewer"], []),

        // finance-eu, finance-us, ... (50): the role of the department, and the region "us" also reads reports directly
        .. Departments.SelectMany((department, d) => Regions.Select(region => (
            Name: $"{department}-{region}",
            DisplayName: $"{Title(department)} {region.ToUpperInvariant()}",
            Roles: new[] { DepartmentRoles[d] },
            Permissions: region == "us" ? new[] { "reports.read" } : []))),
    ];

    /// <summary>The names of all demo groups.</summary>
    public static IReadOnlyList<string> GroupNames { get; } = [.. Groups.Select(g => g.Name)];

    // ---------------------------------------------------------------- people: 6 + 50 = 56

    private static readonly string[] FirstNames =
    [
        "liam", "olivia", "noah", "emma", "oliver", "ava", "elijah", "sophia", "james", "isabella", "william", "mia", "benjamin", "amelia", "lucas", "harper", "henry", "evelyn",
        "theo", "luna", "jack", "camila", "levi", "aria", "alexander", "ella", "mason", "chloe", "ethan", "layla", "daniel", "nora", "jacob", "zoe", "logan", "lily",
        "jackson", "hannah", "sebastian", "stella", "mateo", "violet", "samuel", "grace", "david", "ruby", "joseph", "ivy", "carter", "quinn",
    ];

    private static readonly string[] LastNames =
    [
        "smith", "johnson", "williams", "brown", "jones", "garcia", "miller", "davis", "rodriguez", "martinez", "hernandez", "lopez", "gonzalez", "wilson", "anderson", "thomas",
        "taylor", "moore", "jackson", "martin", "lee", "perez", "thompson", "white", "harris", "sanchez", "clark", "ramirez", "lewis", "robinson", "walker", "young", "allen",
        "king", "wright", "scott", "torres", "nguyen", "hill", "flores", "green", "adams", "nelson", "baker", "hall", "rivera", "campbell", "mitchell", "foster", "roberts",
    ];

    internal static readonly (string UserName, string First, string Last, string? Phone, string[] Roles, string[] Groups, string[] Direct)[] People =
    [
        ("alice", "Alice", "Admin", "+15550100001", ["Admin"], [], []),
        ("bob", "Bob", "Manager", "+15550100002", ["Manager"], ["sales"], []),
        ("carol", "Carol", "Editor", null, ["Editor"], [], []),
        ("dave", "Dave", "Viewer", null, ["Viewer"], ["support"], []),
        ("erin", "Erin", "Finance", null, [], ["finance"], []),
        ("frank", "Frank", "Direct", null, [], [], ["orders.read", "reports.read"]),

        // liam.smith, olivia.johnson, ... (50). Each one gets a role or two, a group or two, and now and then a direct permission.
        .. Enumerable.Range(0, 50).Select(i =>
        {
            var first = FirstNames[i];
            var last = LastNames[i];
            var assignable = RoleNames.Where(r => r != "Admin").ToArray();
            var roles = new List<string> { assignable[(i * 3) % assignable.Length] };
            if (i % 2 == 0) roles.Add(assignable[(i * 7 + 5) % assignable.Length]);
            var memberOf = new List<string> { GroupNames[(i * 11) % GroupNames.Count] };
            if (i % 3 == 0) memberOf.Add(GroupNames[(i * 13 + 7) % GroupNames.Count]);
            var direct = i % 4 == 0 ? new[] { PermissionKeys[(i * 5) % PermissionKeys.Count] } : [];
            return ($"{first}.{last}", Title(first), Title(last), i % 3 == 0 ? $"+1555020{i:0000}" : null, roles.Distinct().ToArray(), memberOf.Distinct().ToArray(), direct);
        }),
    ];

    /// <summary>The user names of all demo people.</summary>
    public static IReadOnlyList<string> UserNames { get; } = [.. People.Select(p => p.UserName)];

    /// <summary>
    /// Adds the demo permissions, roles, groups, scopes and people of the sample project, and gives them their access. Returns how many people were created.
    /// A finished seed (the first and the last person exist in the project) is skipped at once. It always works in <b>its own scope of the services</b>:
    /// the project of a scope is fixed by the first thing that asks for it, so a caller that already worked in another project (the seed of the admin works in
    /// <c>_global</c>) would otherwise put the demo data into that project.
    /// </summary>
    public async Task<int> EnsureAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DemoDataRun>().RunAsync(ct);
    }
}

/// <summary>One run of the demo seed (see <see cref="DemoDataSeeder"/>). Scoped: made fresh for every run.</summary>
internal sealed class DemoDataRun(
    UserManager<AppUser> users, IAccessService access, IGroupService groups, IProjectService projects, IApplicationService applications, Auth2DbContext db,
    IOpenIddictScopeManager scopes, IProjectSelection selection, IGlobalScope globalScope, TimeProvider clock, ILogger<DemoDataRun> log)
{
        /// <summary>Runs the seed.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        selection.ProjectKey = DemoDataSeeder.ProjectKey;   // the stores and the access services work in the selected project

        await RepairMisplacedAsync(ct);

        if (await users.FindByNameAsync(DemoDataSeeder.People[^1].UserName) is not null && await users.FindByNameAsync(DemoDataSeeder.People[0].UserName) is not null) return 0;   // already seeded

        await EnsureScopesAsync(ct);

        var known = (await access.ListPermissionsAsync(ct)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, name, description, group) in DemoDataSeeder.Permissions)
            if (!known.Contains(key)) Check(await access.CreatePermissionAsync(new PermissionDefinition(key, name, description, group), ct), "permission " + key);

        var knownRoles = (await access.ListRolesAsync(ct)).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, description, permissions) in DemoDataSeeder.Roles)
        {
            if (!knownRoles.Contains(name)) Check(await access.CreateRoleAsync(name, description, ct), "role " + name);
            foreach (var key in permissions) Check(await access.AddRolePermissionAsync(name, key, ct), $"role {name} permission {key}");
        }

        var knownGroups = (await groups.ListAsync(ct)).Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, displayName, groupRoles, groupPermissions) in DemoDataSeeder.Groups)
        {
            if (!knownGroups.Contains(name)) Check(await groups.CreateAsync(name, displayName, null, ct), "group " + name);
            foreach (var role in groupRoles) Check(await groups.AddRoleAsync(name, role, ct), $"group {name} role {role}");
            foreach (var key in groupPermissions) Check(await groups.AddPermissionAsync(name, key, ct), $"group {name} permission {key}");
        }

        var created = 0;
        foreach (var (userName, first, last, phone, personRoles, personGroups, direct) in DemoDataSeeder.People)
        {
            var user = await users.FindByNameAsync(userName);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = userName,
                    Email = $"{userName}@example.com",
                    EmailConfirmed = true,
                    FirstName = first,
                    LastName = last,
                    DisplayName = $"{first} {last}",
                    PhoneNumber = phone,
                    PhoneNumberConfirmed = phone is not null,
                    CreatedAtUtc = clock.GetUtcNow(),
                };
                var result = await users.CreateAsync(user, DemoDataSeeder.Password);
                if (!result.Succeeded) { log.LogWarning("Demo user {UserName} was not created: {Errors}", userName, string.Join(", ", result.Errors.Select(e => e.Code))); continue; }
                created++;
            }
            foreach (var role in personRoles) Check(await access.AssignRoleAsync(user, role, ct), $"{userName} role {role}");
            foreach (var group in personGroups) Check(await groups.AddUserAsync(group, user, ct), $"{userName} group {group}");
            foreach (var key in direct) Check(await access.GrantPermissionAsync(user, key, null, ct), $"{userName} permission {key}");
        }

        if (created > 0) log.LogWarning("Demo data added to the project '{Project}': {Count} users with the public password. Never use this on a real server.", DemoDataSeeder.ProjectKey, created);
        return created;
    }

    /// <summary>
    /// An older version of this seed put the demo data into the project <c>_global</c> instead of the sample project (the project of a scope stays the one that was
    /// asked for first, and the seed of the admin asks for <c>_global</c>). When demo people are members of <c>_global</c> and not of the sample project, that data is
    /// removed from <c>_global</c> so it can be added to the right project.
    /// </summary>
    private async Task RepairMisplacedAsync(CancellationToken ct)
    {
        var sample = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == DemoDataSeeder.ProjectKey, ct);
        var global = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectKey == ProjectKeyRules.GlobalProjectKey, ct);
        if (sample is null || global is null) return;

        var names = DemoDataSeeder.UserNames;
        var misplaced = await db.Users
            .Where(u => names.Contains(u.UserName!) && db.UserProjects.Any(x => x.UserId == u.Id && x.ProjectId == global.Id) && !db.UserProjects.Any(x => x.UserId == u.Id && x.ProjectId == sample.Id))
            .Select(u => u.Id).ToListAsync(ct);
        if (misplaced.Count == 0) return;

        try
        {
            await db.Users.Where(u => misplaced.Contains(u.Id)).ExecuteDeleteAsync(ct);
            var roleNames = DemoDataSeeder.RoleNames; var groupNames = DemoDataSeeder.GroupNames; var keys = DemoDataSeeder.PermissionKeys;
            await db.Roles.Where(r => r.ProjectId == global.Id && roleNames.Contains(r.Name!)).ExecuteDeleteAsync(ct);
            await db.Groups.Where(g => g.ProjectId == global.Id && groupNames.Contains(g.Name)).ExecuteDeleteAsync(ct);
            await db.Permissions.Where(p => p.ProjectId == global.Id && keys.Contains(p.Key)).ExecuteDeleteAsync(ct);
            log.LogWarning("Demo data found in the project '_global' was removed ({Count} people); it is added to '{Project}' now.", misplaced.Count, DemoDataSeeder.ProjectKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Demo data in the project '_global' could not be removed. Delete the demo people, roles, groups and permissions of '_global' in the admin console.");
        }
    }

    /// <summary>Registers the demo scopes and allows them for the applications of the sample project (both samples).</summary>
    private async Task EnsureScopesAsync(CancellationToken ct)
    {
        foreach (var name in DemoDataSeeder.ScopeNames)
            if (await scopes.FindByNameAsync(name, ct) is null)
                await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = name, DisplayName = name, Description = "Demo scope of the sample applications." }, ct);

        var project = await projects.FindByProjectKeyAsync(DemoDataSeeder.ProjectKey, ct);
        if (project is null) return;
        foreach (var application in (await applications.ListAsync(project.Id, ct)).Where(a => a.ClientId is "mvc-sample" or "minimal-api-sample"))   // the two sample web applications, not the worker
        {
            var missing = DemoDataSeeder.ScopeNames.Where(n => !application.AllowedScopes.Contains(n, StringComparer.Ordinal)).ToList();
            if (missing.Count > 0) await applications.UpdateAsync(application.Id, new ApplicationSpec("", AllowedScopes: [.. application.AllowedScopes, .. missing]), ct);
        }
    }

    private void Check(OperationResult result, string what)
    {
        if (!result.Succeeded) log.LogWarning("Demo data: {What} was not added ({Code}).", what, result.ErrorCode);
    }
}
