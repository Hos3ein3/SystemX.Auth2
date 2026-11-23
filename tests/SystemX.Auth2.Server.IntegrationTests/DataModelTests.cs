using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Commands;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class DataModelTests : ServerTestBase
{
    private async Task<(Auth2Application a, Auth2Application b)> TwoAppsAsync()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplicationService>();
        return (await svc.CreateAsync("appa", "App A", null, null), await svc.CreateAsync("appb", "App B", null, null));
    }

    [Fact]
    public async Task Migrate_CreatesExpectedTables_AndNoPendingMigrations()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        var tables = await db.Database.SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'").ToListAsync();
        foreach (var t in new[] { "applications", "user_applications", "user_application_recovery_codes", "permissions", "user_permissions",
            "role_permissions", "groups", "user_groups", "group_roles", "group_permissions", "audit_events", "otp_codes",
            "data_protection_keys", "users", "roles", "user_roles", "user_claims", "role_claims", "user_logins", "user_tokens",
            "openiddict_applications", "openiddict_authorizations", "openiddict_scopes", "openiddict_tokens" })
            tables.ShouldContain(t);
    }

    [Fact]
    public async Task Settings_AreStoredAsJsonb_AndRoundTrip()
    {
        var (a, _) = await TwoAppsAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var type = await db.Database.SqlQuery<string>(
            $"SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_name='applications' AND column_name='settings_json'").SingleAsync();
        type.ShouldBe("jsonb");

        var s = new ApplicationSettings();
        s.Password.MinLength = 12;
        await scope.ServiceProvider.GetRequiredService<IApplicationService>().UpdateSettingsAsync(a.Id, s);
        await using var scope2 = NewScope();
        var loaded = await scope2.ServiceProvider.GetRequiredService<IApplicationSettingsProvider>().GetAsync(a.Id);
        loaded.Password.MinLength.ShouldBe(12);
    }

    [Fact]
    public async Task UpdateSettings_BumpsVersion_InvalidIsRejected_CacheIsCleared()
    {
        var (a, _) = await TwoAppsAsync();
        await using var scope = NewScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApplicationService>();
        var provider = scope.ServiceProvider.GetRequiredService<IApplicationSettingsProvider>();

        (await provider.GetAsync(a.Id)).Password.MinLength.ShouldBe(10); // fills the cache
        var s = new ApplicationSettings();
        s.Password.MinLength = 16;
        await svc.UpdateSettingsAsync(a.Id, s);
        (await provider.GetAsync(a.Id)).Password.MinLength.ShouldBe(16);
        (await svc.FindByProjectKeyAsync("appa"))!.SettingsVersion.ShouldBe(2);

        var bad = new ApplicationSettings();
        bad.Lockout.MaxFailedAttempts = 0;
        await Should.ThrowAsync<SettingsValidationException>(() => svc.UpdateSettingsAsync(a.Id, bad));
        (await svc.FindByProjectKeyAsync("appa"))!.SettingsVersion.ShouldBe(2);
    }

    [Theory]
    [InlineData("_global")]
    [InlineData("AB")]
    [InlineData("Has Space")]
    public async Task CreateApplication_InvalidProjectKey_Throws(string key)
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        await Should.ThrowAsync<ArgumentException>(() =>
            scope.ServiceProvider.GetRequiredService<IApplicationService>().CreateAsync(key, "Name", null, null));
    }

    [Fact]
    public async Task CreateApplication_DuplicateProjectKey_Fails()
    {
        await TwoAppsAsync();
        await using var scope = NewScope();
        await Should.ThrowAsync<DbUpdateException>(() =>
            scope.ServiceProvider.GetRequiredService<IApplicationService>().CreateAsync("appa", "Other", null, null));
    }

    [Fact]
    public async Task CreateApplication_CreatesOpenIddictClientAndAudit()
    {
        var (a, _) = await TwoAppsAsync();
        a.OpenIddictApplicationId.ShouldNotBeNull();
        a.ClientId.ShouldBe("appa");
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.AuditEvents.AnyAsync(e => e.Action == "application.created" && e.ApplicationId == a.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Roles_SameNameInTwoApplications_BothExist_AndFilterSeparatesThem()
    {
        var (a, b) = await TwoAppsAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using (scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin())
        {
            db.Roles.Add(new AppRole("writer") { NormalizedName = "WRITER", ApplicationId = a.Id });
            db.Roles.Add(new AppRole("writer") { NormalizedName = "WRITER", ApplicationId = b.Id });
            await db.SaveChangesAsync();
        }

        var appScope = scope.ServiceProvider.GetRequiredService<IApplicationScope>();
        appScope.Set(a.Id);
        var visible = await db.Roles.Where(r => r.Name == "writer").ToListAsync();
        visible.Count.ShouldBe(1);
        visible[0].ApplicationId.ShouldBe(a.Id);
        (await db.Roles.Select(r => r.Name).ToListAsync()).ShouldContain("GlobalAdmin"); // global roles stay visible
    }

    [Fact]
    public async Task Roles_DuplicateNameInSameApplication_IsRejected()
    {
        var (a, _) = await TwoAppsAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        db.Roles.Add(new AppRole("writer") { NormalizedName = "WRITER", ApplicationId = a.Id });
        await db.SaveChangesAsync();
        db.Roles.Add(new AppRole("Writer") { NormalizedName = "WRITER", ApplicationId = a.Id });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Roles_DuplicateGlobalName_IsRejected_ByNullsNotDistinct()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        db.Roles.Add(new AppRole("GlobalAdmin") { NormalizedName = "GLOBALADMIN", ApplicationId = null });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task TenantTables_WithoutScope_ReturnNothing_AndGlobalScopeSeesAll()
    {
        var (a, b) = await TwoAppsAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using (scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin())
        {
            db.Permissions.Add(new Permission { ApplicationId = a.Id, Key = "orders.read" });
            db.Permissions.Add(new Permission { ApplicationId = b.Id, Key = "orders.read" });
            db.Groups.Add(new Group { ApplicationId = a.Id, Name = "Finance" });
            await db.SaveChangesAsync();
            (await db.Permissions.CountAsync()).ShouldBe(2);
        }

        (await db.Permissions.CountAsync()).ShouldBe(0);
        (await db.Groups.CountAsync()).ShouldBe(0);
        (await db.UserApplications.CountAsync()).ShouldBe(0);
        scope.ServiceProvider.GetRequiredService<IApplicationScope>().Set(b.Id);
        (await db.Permissions.Select(p => p.ApplicationId).ToListAsync()).ShouldBe([b.Id]);
        (await db.Groups.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Permissions_DuplicateKeyInSameApplication_IsRejected()
    {
        var (a, _) = await TwoAppsAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        db.Permissions.Add(new Permission { ApplicationId = a.Id, Key = "x.y" });
        db.Permissions.Add(new Permission { ApplicationId = a.Id, Key = "x.y" });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task User_SameUserInTwoApplications_HasTwoIndependentCredentialRows()
    {
        var (a, b) = await TwoAppsAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var user = new AppUser { UserName = "sara", NormalizedUserName = "SARA", Email = "s@e.com", NormalizedEmail = "S@E.COM" };
        db.Users.Add(user);
        db.UserApplications.Add(new UserApplication { UserId = user.Id, ApplicationId = a.Id, PasswordHash = hasher.HashPassword(user, "PassA-12345") });
        db.UserApplications.Add(new UserApplication { UserId = user.Id, ApplicationId = b.Id, PasswordHash = hasher.HashPassword(user, "PassB-12345") });
        await db.SaveChangesAsync();

        (await db.Users.CountAsync(u => u.UserName == "sara")).ShouldBe(1);
        var rows = await db.UserApplications.Where(x => x.UserId == user.Id).ToListAsync();
        rows.Count.ShouldBe(2);
        var rowA = rows.Single(r => r.ApplicationId == a.Id);
        var rowB = rows.Single(r => r.ApplicationId == b.Id);
        hasher.VerifyHashedPassword(user, rowA.PasswordHash!, "PassA-12345").ShouldNotBe(PasswordVerificationResult.Failed);
        hasher.VerifyHashedPassword(user, rowA.PasswordHash!, "PassB-12345").ShouldBe(PasswordVerificationResult.Failed);
        hasher.VerifyHashedPassword(user, rowB.PasswordHash!, "PassB-12345").ShouldNotBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public async Task User_DuplicateEmailOrUserName_IsRejectedGlobally()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        scope.ServiceProvider.GetRequiredService<IApplicationSelection>().ProjectKey = "_global";
        (await users.CreateAsync(new AppUser { UserName = "sara", Email = "sara@example.com" }, "Str0ng-Password!")).Succeeded.ShouldBeTrue();
        var sameEmail = await users.CreateAsync(new AppUser { UserName = "sara2", Email = "SARA@example.com" }, "Str0ng-Password!");
        sameEmail.Errors.ShouldContain(e => e.Code == "DuplicateEmail");
        var sameName = await users.CreateAsync(new AppUser { UserName = "Sara", Email = "other@example.com" }, "Str0ng-Password!");
        sameName.Errors.ShouldContain(e => e.Code == "DuplicateUserName");
    }

    [Fact]
    public async Task MultiApplicationContext_ResolvesFromClientIdOrSelection_AndSetsScope()
    {
        var (a, b) = await TwoAppsAsync();
        await using (var scope = NewScope())
        {
            scope.ServiceProvider.GetRequiredService<IClientIdAccessor>().ClientId = "appb";
            var app = await scope.ServiceProvider.GetRequiredService<IApplicationContext>().GetRequiredAsync();
            app.Id.ShouldBe(b.Id);
            scope.ServiceProvider.GetRequiredService<IApplicationScope>().ApplicationId.ShouldBe(b.Id);
        }
        await using (var scope = NewScope())
        {
            scope.ServiceProvider.GetRequiredService<IApplicationSelection>().ProjectKey = "appa";
            (await scope.ServiceProvider.GetRequiredService<IApplicationContext>().GetRequiredAsync()).Id.ShouldBe(a.Id);
        }
    }

    [Fact]
    public async Task MultiApplicationContext_UnknownOrMissingClient_ThrowsNotFound()
    {
        await TwoAppsAsync();
        await using var scope = NewScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IApplicationContext>();
        await Should.ThrowAsync<ApplicationNotFoundException>(() => ctx.GetRequiredAsync());
        scope.ServiceProvider.GetRequiredService<IClientIdAccessor>().ClientId = "nope";
        await Should.ThrowAsync<ApplicationNotFoundException>(() => ctx.GetRequiredAsync());
    }

    [Fact]
    public async Task Commands_MigrateSeedAndCreateApplication_Work()
    {
        (await Auth2Commands.RunAsync(["migrate"], Factory.Services)).ShouldBe(0);
        (await Auth2Commands.RunAsync(["seed"], Factory.Services)).ShouldBe(0);
        (await Auth2Commands.RunAsync(["create-application", "papertrade", "PaperTrade"], Factory.Services)).ShouldBe(0);
        (await Auth2Commands.RunAsync(["create-application", "papertrade"], Factory.Services)).ShouldBe(2);
        (await Auth2Commands.RunAsync(["create-application", "_global", "X"], Factory.Services)).ShouldBe(1);

        await using var scope = NewScope();
        (await scope.ServiceProvider.GetRequiredService<IApplicationService>().FindByProjectKeyAsync("papertrade")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Initializer_RunTwiceInParallel_DoesNotFail()
    {
        await Task.WhenAll(MigrateAndSeedAsync(), MigrateAndSeedAsync());
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db.Users.CountAsync()).ShouldBe(1);
    }
}
