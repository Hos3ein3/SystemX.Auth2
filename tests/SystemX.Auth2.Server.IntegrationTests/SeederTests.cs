using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

public class SeederMultiModeTests : ServerTestBase
{
    private const string DefaultPassword = "Admin@12345!ChangeMe";

    [Fact]
    public async Task Seed_FirstRun_CreatesGlobalProjectRoleUserAndScopes()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<Auth2DbContext>();
        using var _ = sp.GetRequiredService<IGlobalScope>().Begin();

        var global = await db.Applications.SingleAsync();
        global.ProjectKey.ShouldBe("_global");
        global.OpenIddictApplicationId.ShouldNotBeNull();

        var role = await db.Roles.SingleAsync(r => r.Name == Auth2Roles.GlobalAdmin);
        role.ApplicationId.ShouldBeNull();
        role.IsSystem.ShouldBeTrue();

        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var admin = (await users.FindByNameAsync("globaladmin"))!;
        admin.EmailConfirmed.ShouldBeTrue();
        (await users.IsInRoleAsync(admin, Auth2Roles.GlobalAdmin)).ShouldBeTrue();

        var scopes = sp.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var name in Auth2Scopes.All.Append("openid").Append("offline_access"))
            (await scopes.FindByNameAsync(name)).ShouldNotBeNull(name);
    }

    [Fact]
    public async Task Seed_MultiMode_MovesCredentialsToUserApplicationsRow()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();

        var admin = await db.Users.SingleAsync();
        admin.PasswordHash.ShouldBeNull();
        var ua = await db.UserApplications.SingleAsync();
        ua.UserId.ShouldBe(admin.Id);
        ua.PasswordHash.ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>()
            .VerifyHashedPassword(admin, ua.PasswordHash!, DefaultPassword).ShouldNotBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public async Task Seed_SecondRun_ChangesNothing()
    {
        await MigrateAndSeedAsync();
        async Task<(int apps, int users, int roles, int uas, int audits, string? hash)> Snapshot()
        {
            await using var scope = NewScope();
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            return (await db.Applications.CountAsync(), await db.Users.CountAsync(), await db.Roles.CountAsync(),
                await db.UserApplications.CountAsync(), await db.AuditEvents.CountAsync(),
                await db.UserApplications.Select(x => x.PasswordHash).SingleAsync());
        }
        var before = await Snapshot();
        await SeedAgainAsync();
        (await Snapshot()).ShouldBe(before);
    }

    [Fact]
    public async Task Seed_AdminWithChangedPassword_KeepsNewPassword()
    {
        await MigrateAndSeedAsync();
        string newHash;
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var admin = await db.Users.SingleAsync();
            var ua = await db.UserApplications.SingleAsync();
            newHash = ua.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>().HashPassword(admin, "Another!Pass99");
            await db.SaveChangesAsync();
        }

        await SeedAgainAsync();

        await using var scope2 = NewScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var __ = scope2.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db2.UserApplications.SingleAsync()).PasswordHash.ShouldBe(newHash);
        (await db2.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Seed_WritesAuditEvent_WithoutPassword()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var events = await db.AuditEvents.Where(a => a.Action == "seed.global_admin_created").ToListAsync();
        events.Count.ShouldBe(1);
        (events[0].DataJson ?? "").ShouldNotContain(DefaultPassword);
        events[0].TargetType.ShouldBe("user");
    }

    [Fact]
    public async Task Seed_LogsWarning_AndNeverLogsDefaultPassword()
    {
        await MigrateAndSeedAsync();
        Factory.Logs.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("Default GlobalAdmin created"));
        Factory.Logs.Entries.ShouldNotContain(e => e.Message.Contains(DefaultPassword));
    }

    [Fact]
    public async Task WarnIfDefaultPassword_DefaultStillSet_LogsWarning()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<Persistence.Seeding.Auth2Seeder>().WarnIfDefaultPasswordAsync();
        Factory.Logs.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("still has the default password"));
    }

    [Fact]
    public async Task Seed_ExistingNonAdminWithSameName_IsNotTouched()
    {
        await MigrateAndSeedAsync();
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
            var admin = await db.Users.SingleAsync();
            db.UserRoles.RemoveRange(db.UserRoles);
            await db.SaveChangesAsync();
        }
        await SeedAgainAsync();
        await using var scope2 = NewScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var __ = scope2.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        (await db2.Users.CountAsync()).ShouldBe(1);
        (await db2.UserRoles.CountAsync()).ShouldBe(0);
    }
}

public class SeederSingleModeTests : ServerTestBase
{
    protected override bool SingleMode => true;

    [Fact]
    public async Task Seed_SingleMode_CreatesFixedApplicationAndNoUserApplications()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();

        (await db.Applications.Select(a => a.ProjectKey).ToListAsync()).OrderBy(x => x).ShouldBe(["_global", "default"]);
        (await db.Applications.SingleAsync(a => a.ProjectKey == "default")).Name.ShouldBe("Default");
        (await db.UserApplications.CountAsync()).ShouldBe(0);
        (await db.Users.SingleAsync()).PasswordHash.ShouldNotBeNull();
    }

    [Fact]
    public async Task SingleApplicationContext_ResolvesFixedApplication_AndSetsScope()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        var app = await scope.ServiceProvider.GetRequiredService<IApplicationContext>().GetRequiredAsync();
        app.ProjectKey.ShouldBe("default");
        scope.ServiceProvider.GetRequiredService<IApplicationScope>().ApplicationId.ShouldBe(app.Id);
    }

    [Fact]
    public async Task CreateApplication_InSingleMode_Throws()
    {
        await MigrateAndSeedAsync();
        await using var scope = NewScope();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IApplicationService>().CreateAsync("another", "Another", null, null));
    }
}
