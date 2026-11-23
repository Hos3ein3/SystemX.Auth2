using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure;

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
    IPasswordHasher<AppUser> hasher,
    TimeProvider clock)
{
    // Hard-coded first admin. Change before production (see docs/seeding.md).
    internal const string AdminUserName = "globaladmin";
    internal const string AdminEmail = "globaladmin@systemx.local";
    internal const string AdminPassword = "Admin@12345!ChangeMe";

    /// <summary>Runs the seed in one transaction.</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var single = options.Value.Mode == ServerMode.SingleApplication;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var globalApp = await EnsureApplicationAsync(ProjectKeyRules.GlobalProjectKey, "Global", "Built-in project for developers and global admins.", ct);
        await EnsureScopesAsync(ct);
        await EnsureGlobalAdminRoleAsync(ct);
        if (single)
            await EnsureApplicationAsync(SlugFor(options.Value.SingleApplication.Name), options.Value.SingleApplication.Name, null, ct);
        var created = await EnsureAdminAsync(globalApp, single, ct);

        await tx.CommitAsync(ct);

        if (created)
            log.LogWarning("Default GlobalAdmin created with the default password. Change it now.");
    }

    /// <summary>Logs a warning when the admin still has the default password. Never stops the application.</summary>
    public async Task WarnIfDefaultPasswordAsync(CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var admin = await users.FindByNameAsync(AdminUserName);
        if (admin is null) return;

        var hash = admin.PasswordHash;
        if (options.Value.Mode == ServerMode.MultiApplication && options.Value.CredentialScope == CredentialScope.PerApplication)
        {
            var globalApp = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectKey == ProjectKeyRules.GlobalProjectKey, ct);
            if (globalApp is not null)
                hash = await db.UserApplications.Where(x => x.UserId == admin.Id && x.ApplicationId == globalApp.Id)
                    .Select(x => x.PasswordHash).FirstOrDefaultAsync(ct);
        }
        if (hash is null) return;

        var result = hasher.VerifyHashedPassword(admin, hash, AdminPassword);
        if (result != PasswordVerificationResult.Failed)
            log.LogWarning("The GlobalAdmin user still has the default password. Change it now.");
    }

    private async Task<Auth2Application> EnsureApplicationAsync(string projectKey, string name, string? description, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.ProjectKey == projectKey, ct);
        if (app is null)
        {
            app = new Auth2Application
            {
                ProjectKey = projectKey,
                Name = name,
                Description = description,
                ClientId = projectKey,
                CreatedAtUtc = clock.GetUtcNow(),
            };
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }
        if (app.OpenIddictApplicationId is null)
        {
            app.OpenIddictApplicationId = await provisioner.EnsureClientAsync(app, ct);
            await db.SaveChangesAsync(ct);
        }
        return app;
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
            ApplicationId = null,
            IsSystem = true,
            Description = "Can manage everything on this server.",
        });
        EnsureSucceeded(result, "create the GlobalAdmin role");
        await Task.CompletedTask;
    }

    private async Task<bool> EnsureAdminAsync(Auth2Application globalApp, bool single, CancellationToken ct)
    {
        // Never create a second admin and never touch an existing one.
        if ((await users.GetUsersInRoleAsync(Auth2Roles.GlobalAdmin)).Count > 0) return false;
        if (await users.FindByNameAsync(AdminUserName) is not null || await users.FindByEmailAsync(AdminEmail) is not null)
        {
            log.LogWarning("A user named '{UserName}' exists but is not a GlobalAdmin. The seeder did not change it.", AdminUserName);
            return false;
        }

        var user = new AppUser
        {
            UserName = AdminUserName,
            Email = AdminEmail,
            EmailConfirmed = true,
            IsActive = true,
            DisplayName = "Global admin",
            CreatedAtUtc = clock.GetUtcNow(),
        };
        EnsureSucceeded(await users.CreateAsync(user, AdminPassword), "create the GlobalAdmin user");
        EnsureSucceeded(await users.AddToRoleAsync(user, Auth2Roles.GlobalAdmin), "add the user to GlobalAdmin");

        if (!single)
        {
            var ua = new UserApplication
            {
                UserId = user.Id,
                ApplicationId = globalApp.Id,
                JoinedAtUtc = clock.GetUtcNow(),
                Status = UserApplicationStatus.Active,
                LockoutEnabled = true,
            };
            if (options.Value.CredentialScope == CredentialScope.PerApplication)
            {
                // Credentials live in user_applications; the user row keeps none.
                ua.PasswordHash = user.PasswordHash;
                ua.SecurityStamp = user.SecurityStamp;
                user.PasswordHash = null;
            }
            db.UserApplications.Add(ua);
            await db.SaveChangesAsync(ct);
        }

        await audit.WriteAsync(new AuditEntry("seed.global_admin_created", globalApp.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return true;
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
