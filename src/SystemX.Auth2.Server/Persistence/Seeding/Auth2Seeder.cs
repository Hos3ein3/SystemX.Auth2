using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Caching;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;

namespace SystemX.Auth2.Server.Persistence.Seeding;

/// <summary>
/// Creates the data the server needs on first start. Safe to run many times (idempotent).
/// See <c>docs/seeding.md</c>, including how to move the admin values to environment variables.
/// </summary>
public sealed partial class Auth2Seeder(
    Auth2DbContext db,
    UserManager<AppUser> users,
    RoleManager<AppRole> roles,
    IOptions<Auth2ServerOptions> options,
    ILogger<Auth2Seeder> log,
    IGlobalScope globalScope,
    IClientProvisioner provisioner,
    IOpenIddictScopeManager scopeManager,
    IAuditService audit,
    IProjectSelection selection,
    TimeProvider clock,
    IServiceProvider services,
    IOpenIddictApplicationManager? appManager = null,
    IHostEnvironment? env = null)
{
    // Hard-coded first admin. Change before production (see docs/seeding.md).
    internal const string AdminUserName = "globaladmin";
    internal const string AdminEmail = "globaladmin@systemx.local";
    internal const string AdminPassword = "Admin@12345!ChangeMe";

    /// <summary>Runs the seed in one transaction.</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var single = options.Value.Mode == ServerMode.SingleProject;

        await EnsureDatabaseCompatibilityAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var seedDemo = false;

        var globalApp = await EnsureProjectAsync(ProjectKeyRules.GlobalProjectKey, "Global", "Built-in project for developers and global admins.", "_global", ct);
        await EnsureScopesAsync(ct);
        await EnsureGlobalAdminRoleAsync(ct);
        await EnsureDeveloperRoleAsync(ct);
        if (single)
            await EnsureProjectAsync(SlugFor(options.Value.SingleProject.Name), options.Value.SingleProject.Name, null, "default", ct);
        var created = await EnsureAdminAsync(globalApp, single, ct);

        if (env is null || !env.IsEnvironment("Testing") || string.Equals(Environment.GetEnvironmentVariable("AUTH2_SEED_SAMPLES"), "true", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureGlobalPermissionsAsync(globalApp, ct);
            await EnsureSampleProjectAndApplicationsAsync(ct);
            seedDemo = !single && options.Value.SeedDemoData;
        }

        await tx.CommitAsync(ct);
        await ForgetCachedDataAsync(ct);

        // The demo people of the sample project. The sample project is not a real project, so they are added wherever it is added (switch off with Auth2:SeedDemoData=false).
        // They come after the commit and cannot fail the seed: a problem here is logged and the server starts.
        if (seedDemo)
        {
            try { await services.GetRequiredService<DemoDataSeeder>().EnsureAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "The demo data of the sample project could not be added. The server starts without it."); }
        }

        if (created)
            log.LogWarning("Default GlobalAdmin created with the default password. Change it now.");
    }

    private async Task EnsureDatabaseCompatibilityAsync(CancellationToken ct)
    {
        try
        {
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE projects ALTER COLUMN version SET DEFAULT '0'::xid;", ct);
            }
        }
        catch
        {
            // Ignore if column does not exist or database is not relational/PostgreSQL
        }
    }

    /// <summary>
    /// Seeds the sample project and the two sample applications (sample mvc and sample api) with their settings.
    /// </summary>
    public async Task EnsureSampleProjectAndApplicationsAsync(CancellationToken ct = default)
    {
        var single = options.Value.Mode == ServerMode.SingleProject;
        var projectKey = single ? SlugFor(options.Value.SingleProject.Name) : "sample-project";
        var projectName = single ? options.Value.SingleProject.Name : "Sample Project";
        var projectDesc = single ? null : "Sample project for MVC and Minimal API applications.";

        var project = await db.Projects.FirstOrDefaultAsync(a => a.ProjectKey == projectKey, ct);
        if (project is null)
        {
            var now = clock.GetUtcNow();
            project = new Project
            {
                ProjectKey = projectKey,
                Name = projectName,
                Description = projectDesc,
                IsEnabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Settings = new ProjectSettings
                {
                    Clients = new ClientSettings
                    {
                        GroupSyncAllowed = true,
                        PermissionSyncAllowed = true,
                        RoleSyncAllowed = true,
                        ScopeSyncAllowed = true,
                        DefaultUiMode = UiMode.Hosted
                    },
                    Sessions = new SessionSettings
                    {
                        CookieLifetimeMinutes = 480,
                        AccessTokenMinutes = 60,
                        RefreshTokenDays = 30
                    },
                    Registration = new RegistrationSettings
                    {
                        RegistrationEnabled = true,
                        JoinMode = JoinMode.AutoJoin
                    }
                }
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
        }

        // MVC Application
        await EnsureApplicationAsync(
            project,
            name: "MvcSample",
            clientId: "mvc-sample",
            secret: "mvc-sample-secret",
            redirectUris:
            [
                "http://localhost:5101/signin-oidc",
                "https://localhost:5101/signin-oidc",
                "http://sample-mvc:5001/signin-oidc",
                "http://localhost:5001/signin-oidc"
            ],

            postLogoutRedirectUris:
            [
                "http://localhost:5101/signout-callback-oidc",
                "https://localhost:5101/signout-callback-oidc",
                "http://sample-mvc:5001/signout-callback-oidc",
                "http://localhost:5001/signout-callback-oidc"
            ],
            allowedScopes:
            [
                .. OpenIddictClientProvisioner.StandardScopes,
                "reports.read",
                "orders.read",
                "orders.write"
            ],
            settings: new ClientApplicationSettings
            {
                UiMode = UiMode.Hosted,
                AllowSelfUiLogin = true,
                CookieLifetimeMinutes = 480,
                AccessTokenMinutes = 60,
                RefreshTokenDays = 30,
                AllowedCorsOrigins = ["http://localhost:5101", "http://sample-mvc:5001", "http://localhost:5001"]
            },
            ct, type: Contracts.Enums.ApplicationType.Web);

        // Minimal API Application
        await EnsureApplicationAsync(
            project,
            name: "MinimalApiSample",
            clientId: "minimal-api-sample",
            secret: "minimal-api-sample-secret",
            redirectUris: [],
            postLogoutRedirectUris: [],
            allowedScopes:
            [
                .. OpenIddictClientProvisioner.StandardScopes,
                "reports.read",
                "orders.read",
                "orders.write"
            ],
            settings: new ClientApplicationSettings
            {
                UiMode = UiMode.None,
                AllowSelfUiLogin = true,
                AccessTokenMinutes = 60,
                RefreshTokenDays = 30,
                AllowedCorsOrigins = ["http://localhost:5102", "http://sample-minimalapi:5002", "http://localhost:5002"]
            },
            ct, type: Contracts.Enums.ApplicationType.Api);

        // Worker application (background job, machine to machine): no UI, no redirect addresses, only the scopes the job needs.
        await EnsureApplicationAsync(
            project,
            name: "WorkerSample",
            clientId: "worker-sample",
            secret: "worker-sample-secret",
            redirectUris: [],
            postLogoutRedirectUris: [],
            allowedScopes: ["auth2.client.handshake", "auth2.permissions.sync", "auth2.roles.sync", "reports.read", "jobs.run"],
            settings: new ClientApplicationSettings
            {
                UiMode = UiMode.None,
                AllowSelfUiLogin = false,
                AccessTokenMinutes = 60,
            },
            ct, type: Contracts.Enums.ApplicationType.Service);

        // Ensure custom scopes
        // The sample scopes are meant for two sample APIs: their resources become audiences of the tokens that have the scopes (docs/scope-resources.md).
        // jobs.run is for the Worker sample (machine to machine): its resource is the client id of the MinimalApi sample, so a token with this scope is accepted by that API.
        foreach (var (scope, resource) in new[] { ("reports.read", "reports-api"), ("orders.read", "orders-api"), ("orders.write", "orders-api"), ("jobs.run", "minimal-api-sample") })
        {
            var existing = await scopeManager.FindByNameAsync(scope, ct);
            if (existing is null)
            {
                var descriptor = new OpenIddictScopeDescriptor { Name = scope, DisplayName = scope };
                descriptor.Resources.Add(resource);
                await scopeManager.CreateAsync(descriptor, ct);
            }
            else if ((await scopeManager.GetResourcesAsync(existing, ct)).IsDefaultOrEmpty)
            {
                var descriptor = new OpenIddictScopeDescriptor();
                await scopeManager.PopulateAsync(descriptor, existing, ct);
                descriptor.Resources.Add(resource);
                await scopeManager.UpdateAsync(existing, descriptor, ct);
            }
        }

        // Ensure globaladmin has access to the sample project with password hash
        var admin = await users.FindByNameAsync(AdminUserName);
        if (admin is not null && !single)
        {
            var adminPassword = Environment.GetEnvironmentVariable("AUTH2_ADMIN_PASSWORD") ?? AdminPassword;
            var adminHash = users.PasswordHasher.HashPassword(admin, adminPassword);
            var sampleUp = await db.UserProjects.FirstOrDefaultAsync(x => x.UserId == admin.Id && x.ProjectId == project.Id, ct);
            if (sampleUp is null)
            {
                db.UserProjects.Add(new UserProject
                {
                    UserId = admin.Id,
                    ProjectId = project.Id,
                    JoinedAtUtc = clock.GetUtcNow(),
                    Status = UserProjectStatus.Active,
                    LockoutEnabled = true,
                    PasswordHash = adminHash,
                });
                await db.SaveChangesAsync(ct);
            }
            else if (string.IsNullOrEmpty(sampleUp.PasswordHash))
            {
                sampleUp.PasswordHash = adminHash;
                await db.SaveChangesAsync(ct);
            }
        }

        // Ensure Admin role for the sample project
        if (!await roles.Roles.AnyAsync(r => r.ProjectId == project.Id && r.Name == "Admin", ct))
        {
            await roles.CreateAsync(new AppRole("Admin")
            {
                ProjectId = project.Id,
                IsSystem = false,
                Description = "Administrator of the sample project.",
            });
        }

        // Ensure sample permissions for sample project
        var samplePerms = new (string Key, string DisplayName, string Description, string Group)[]
        {
            ("reports.read", "Read Reports", "Access and view system reports", "reporting"),
            ("orders.read", "Read Orders", "View customer orders", "commerce"),
            ("orders.write", "Write Orders", "Create and modify customer orders", "commerce"),
        };

        var createdSamplePerms = new List<Permission>();
        foreach (var (k, name, desc, grp) in samplePerms)
        {
            var p = await db.Permissions.FirstOrDefaultAsync(x => x.ProjectId == project.Id && x.Key == k, ct);
            if (p is null)
            {
                p = new Permission
                {
                    ProjectId = project.Id,
                    Key = k,
                    DisplayName = name,
                    Description = desc,
                    Group = grp,
                    Source = PermissionSource.Manual
                };
                db.Permissions.Add(p);
            }
            createdSamplePerms.Add(p);
        }
        await db.SaveChangesAsync(ct);

        var sampleAdminRole = await roles.Roles.FirstOrDefaultAsync(r => r.ProjectId == project.Id && r.Name == "Admin", ct);
        if (sampleAdminRole is not null)
        {
            foreach (var perm in createdSamplePerms)
            {
                if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == sampleAdminRole.Id && rp.PermissionId == perm.Id, ct))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = sampleAdminRole.Id, PermissionId = perm.Id });
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The seed changes projects and applications directly. The copies in the cache (memory and Redis, which outlives a restart) are dropped, so the next request reads what the seed wrote.
    /// </summary>
    private async Task ForgetCachedDataAsync(CancellationToken ct)
    {
        var cache = services.GetService<IHybridCacheService>();
        if (cache is null) return;
        foreach (var p in await db.Projects.AsNoTracking().Select(p => new { p.Id, p.ProjectKey, p.Name }).ToListAsync(ct))
        {
            cache.Invalidate($"settings:{p.Id:N}");
            cache.InvalidateProjectCompletely(p.Id, p.ProjectKey);
            cache.Invalidate(AuthFlowCache.Single(p.Name));
        }
        foreach (var clientId in await db.Set<Auth2Application>().AsNoTracking().Select(a => a.ClientId!).ToListAsync(ct))
            cache.InvalidateClientCompletely(clientId);
    }

    private async Task EnsureApplicationAsync(
        Project project,
        string name,
        string clientId,
        string secret,
        IReadOnlyList<string>? redirectUris,
        IReadOnlyList<string>? postLogoutRedirectUris,
        IReadOnlyList<string>? allowedScopes,
        ClientApplicationSettings settings,
        CancellationToken ct,
        Contracts.Enums.ApplicationType type = Contracts.Enums.ApplicationType.Other)
    {
        var existing = await db.Set<Auth2Application>().FirstOrDefaultAsync(a => a.ClientId == clientId, ct);
        if (existing is null)
        {
            var spec = new ApplicationSpec(name, clientId, redirectUris, postLogoutRedirectUris, allowedScopes, settings, type);
            await provisioner.CreateAsync(project, spec, clock.GetUtcNow(), ct);
        }
        else
        {
            bool modified = false;
            if (existing.ProjectId != project.Id)
            {
                existing.ProjectId = project.Id;
                modified = true;
            }
            if (existing.Name != name)
            {
                existing.Name = name;
                modified = true;
            }
            if (existing.Kind == Contracts.Enums.ApplicationType.Other && type != Contracts.Enums.ApplicationType.Other)
            {
                existing.Kind = type;   // a type an admin or the client already set is not touched
                modified = true;
            }
            if (!existing.IsEnabled)
            {
                existing.IsEnabled = true;
                modified = true;
            }
            if (modified)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        if (appManager is not null)
        {
            var entity = await appManager.FindByClientIdAsync(clientId, ct);
            if (entity is not null)
            {
                await appManager.UpdateAsync(entity, secret, ct);
            }
        }
    }

    /// <summary>Logs a warning when the admin still has the default password. Never stops the application.</summary>
    public async Task WarnIfDefaultPasswordAsync(CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        selection.ProjectKey = ProjectKeyRules.GlobalProjectKey;
        var admin = await users.FindByNameAsync(AdminUserName);
        if (admin is null) return;
        if (await users.CheckPasswordAsync(admin, AdminPassword))
            log.LogWarning("The GlobalAdmin user still has the default password. Change it now.");
    }

    private async Task<Project> EnsureProjectAsync(string projectKey, string name, string? description, string applicationName, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(a => a.ProjectKey == projectKey, ct);
        if (project is null)
        {
            var now = clock.GetUtcNow();
            project = new Project { ProjectKey = projectKey, Name = name, Description = description, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
        }
        // The seeded application uses the project key as client id. Its secret is not shown (rotate it with the rotate-client-secret command).
        if (!await db.Set<Auth2Application>().AnyAsync(a => a.ProjectId == project.Id, ct))
            await provisioner.CreateAsync(project, new ApplicationSpec(applicationName, projectKey), clock.GetUtcNow(), ct);
        return project;
    }

    private async Task EnsureScopesAsync(CancellationToken ct)
    {
        foreach (var name in OpenIddictClientProvisioner.StandardScopes)
        {
            if (await scopeManager.FindByNameAsync(name, ct) is not null) continue;
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor { Name = name, DisplayName = name }, ct);
        }
    }

    private async Task EnsureGlobalAdminRoleAsync(CancellationToken ct)
    {
        if (await roles.FindByNameAsync(Auth2Roles.GlobalAdmin) is not null) return;
        var result = await roles.CreateAsync(new AppRole(Auth2Roles.GlobalAdmin)
        {
            ProjectId = null,
            IsSystem = true,
            Description = "Can manage everything on this server.",
        });
        EnsureSucceeded(result, "create the GlobalAdmin role");
        await Task.CompletedTask;
    }

    private async Task EnsureDeveloperRoleAsync(CancellationToken ct)
    {
        if (await roles.FindByNameAsync(Auth2Roles.Developer) is not null) return;
        var result = await roles.CreateAsync(new AppRole(Auth2Roles.Developer)
        {
            ProjectId = null,
            IsSystem = true,
            Description = "Developer role with access to developer dashboard and server management.",
        });
        EnsureSucceeded(result, "create the Developer role");
        await Task.CompletedTask;
    }

    private async Task EnsureGlobalPermissionsAsync(Project globalApp, CancellationToken ct)
    {
        var perms = new (string Key, string DisplayName, string Description, string Group)[]
        {
            ("users.manage", "Manage Users", "Create, edit, and lock user accounts", "identity"),
            ("roles.manage", "Manage Roles", "Create, edit, and assign roles and permissions", "access"),
            ("audit.view", "View Audit Logs", "Read security and system audit events", "monitoring"),
            ("settings.manage", "Manage Settings", "Configure server and project settings", "system"),
        };

        var createdPerms = new List<Permission>();
        foreach (var (k, name, desc, grp) in perms)
        {
            var p = await db.Permissions.FirstOrDefaultAsync(x => x.ProjectId == globalApp.Id && x.Key == k, ct);
            if (p is null)
            {
                p = new Permission
                {
                    ProjectId = globalApp.Id,
                    Key = k,
                    DisplayName = name,
                    Description = desc,
                    Group = grp,
                    Source = PermissionSource.Manual
                };
                db.Permissions.Add(p);
            }
            createdPerms.Add(p);
        }
        await db.SaveChangesAsync(ct);

        var devRole = await roles.FindByNameAsync(Auth2Roles.Developer);
        var adminRole = await roles.FindByNameAsync(Auth2Roles.GlobalAdmin);

        foreach (var role in new[] { devRole, adminRole })
        {
            if (role is null) continue;
            foreach (var perm in createdPerms)
            {
                if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id, ct))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> EnsureAdminAsync(Project globalApp, bool single, CancellationToken ct)
    {
        var adminPassword = Environment.GetEnvironmentVariable("AUTH2_ADMIN_PASSWORD") ?? AdminPassword;
        selection.ProjectKey = ProjectKeyRules.GlobalProjectKey;   // role and credential reads of the existing admin need a project

        // Never create a second admin. If admin exists, ensure password hash and Developer role are present.
        var existingAdmin = await users.FindByNameAsync(AdminUserName) ?? await users.FindByEmailAsync(AdminEmail);
        if (existingAdmin is not null && !await users.IsInRoleAsync(existingAdmin, Auth2Roles.GlobalAdmin))
        {
            log.LogWarning("A user named '{UserName}' exists but is not a GlobalAdmin. The seeder did not change it.", AdminUserName);
            return false;
        }
        if (existingAdmin is not null)
        {
            var adminHash = users.PasswordHasher.HashPassword(existingAdmin, adminPassword);
            var updated = false;

            if (!await users.IsInRoleAsync(existingAdmin, Auth2Roles.Developer))
            {
                await users.AddToRoleAsync(existingAdmin, Auth2Roles.Developer);
            }

            if (single && string.IsNullOrEmpty(existingAdmin.PasswordHash))
            {
                existingAdmin.PasswordHash = adminHash;
                await users.UpdateAsync(existingAdmin);
            }

            var globalUp = await db.UserProjects.FirstOrDefaultAsync(x => x.UserId == existingAdmin.Id && x.ProjectId == globalApp.Id, ct);
            if (globalUp is not null)
            {
                if (string.IsNullOrEmpty(globalUp.PasswordHash))
                {
                    globalUp.PasswordHash = adminHash;
                    updated = true;
                }
            }
            else if (!single)
            {
                db.UserProjects.Add(new UserProject
                {
                    UserId = existingAdmin.Id,
                    ProjectId = globalApp.Id,
                    JoinedAtUtc = clock.GetUtcNow(),
                    Status = UserProjectStatus.Active,
                    LockoutEnabled = true,
                    PasswordHash = adminHash,
                });
                updated = true;
            }

            if (updated)
            {
                await db.SaveChangesAsync(ct);
            }

            return false;
        }

        // Credentials are written by the user store, to the place the credential scope says.
        selection.ProjectKey = ProjectKeyRules.GlobalProjectKey;
        var user = new AppUser
        {
            UserName = AdminUserName,
            Email = AdminEmail,
            EmailConfirmed = true,
            IsActive = true,
            DisplayName = "Global admin",
            CreatedAtUtc = clock.GetUtcNow(),
        };
        EnsureSucceeded(await users.CreateAsync(user, adminPassword), "create the GlobalAdmin user");
        EnsureSucceeded(await users.AddToRoleAsync(user, Auth2Roles.GlobalAdmin), "add the user to GlobalAdmin");
        EnsureSucceeded(await users.AddToRoleAsync(user, Auth2Roles.Developer), "add the user to Developer");

        // In multi mode the user also belongs to the _global project. With per-application credentials the store
        // already saved this row together with the user; with shared credentials we add it here.
        var globalUserProject = await db.UserProjects.FirstOrDefaultAsync(x => x.UserId == user.Id && x.ProjectId == globalApp.Id, ct);
        var hash = users.PasswordHasher.HashPassword(user, adminPassword);
        if (globalUserProject is not null)
        {
            if (string.IsNullOrEmpty(globalUserProject.PasswordHash))
            {
                globalUserProject.PasswordHash = hash;
                await db.SaveChangesAsync(ct);
            }
        }
        else if (!single)
        {
            db.UserProjects.Add(new UserProject
            {
                UserId = user.Id,
                ProjectId = globalApp.Id,
                JoinedAtUtc = clock.GetUtcNow(),
                Status = UserProjectStatus.Active,
                LockoutEnabled = true,
                PasswordHash = hash,
            });
            await db.SaveChangesAsync(ct);
        }

        await audit.WriteAsync(new AuditEntry("seed.global_admin_created", globalApp.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return true;
    }

    /// <summary>
    /// Adds one more GlobalAdmin (the <c>create-admin</c> command). Fails when the user name or email is taken or the password is weak.
    /// The password is never logged or written to the audit log.
    /// </summary>
    public async Task CreateAdminAsync(string userName, string email, string password, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var single = options.Value.Mode == SystemX.Auth2.Contracts.Enums.ServerMode.SingleProject;
        var globalApp = await db.Projects.FirstAsync(a => a.ProjectKey == ProjectKeyRules.GlobalProjectKey, ct);
        selection.ProjectKey = ProjectKeyRules.GlobalProjectKey;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = new AppUser { UserName = userName, Email = email, EmailConfirmed = true, IsActive = true, DisplayName = "Global admin", CreatedAtUtc = clock.GetUtcNow() };
        EnsureSucceeded(await users.CreateAsync(user, password), "create the admin user");
        EnsureSucceeded(await users.AddToRoleAsync(user, Auth2Roles.GlobalAdmin), "add the user to GlobalAdmin");
        if (!single && !await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == globalApp.Id, ct))
        {
            db.UserProjects.Add(new UserProject { UserId = user.Id, ProjectId = globalApp.Id, JoinedAtUtc = clock.GetUtcNow(), Status = UserProjectStatus.Active, LockoutEnabled = true });
            await db.SaveChangesAsync(ct);
        }
        await audit.WriteAsync(new AuditEntry("admin.created", globalApp.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        await tx.CommitAsync(ct);
    }

    private static void EnsureSucceeded(IdentityResult result, string what)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Seeding failed to {what}: " + string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    /// <summary>Makes a valid project key from a name, for example "My App" gives "my-app".</summary>
    internal static string SlugFor(string name)
    {
        var slug = NonKeyChars().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length < 3) slug = "default";
        return slug.Length > 64 ? slug[..64] : slug;
    }

    [GeneratedRegex("[^a-z0-9_-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex NonKeyChars();
}
