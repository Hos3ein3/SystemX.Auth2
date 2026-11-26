using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Server.Persistence.Seeding;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The name of a project is unique on the server (without letter case and without spaces around), in the services, in the database and in the seed.</summary>
public class ProjectNameTests : Phase6Base
{
    private async Task<(OperationResult Result, string? Key)> CreateAsync(string key, string name)
    {
        await using var scope = NewScope();
        var (result, response) = await scope.ServiceProvider.GetRequiredService<IAdminProjectService>().CreateAsync(new CreateProjectRequest(key, name, null));
        return (result, response?.Project.ProjectKey);
    }

    [Fact]
    public async Task ASecondProjectWithTheSameName_IsRefused_WhateverTheLettersAndSpaces()
    {
        await PrepareAppsAsync();
        (await CreateAsync("shop-one", "Shop")).Result.Succeeded.ShouldBeTrue();

        foreach (var name in new[] { "Shop", "shop", "SHOP", "  Shop  ", " sHoP" })
        {
            var (result, _) = await CreateAsync("shop-two", name);
            result.ErrorCode.ShouldBe(Auth2ErrorCodes.ProjectNameTaken, name);
        }
        (await CreateAsync("shop-two", "Shop Two")).Result.Succeeded.ShouldBeTrue();   // another name is fine
    }

    [Fact]
    public async Task TheServiceThatCreatesProjects_ThrowsForATakenName()
    {
        await PrepareAppsAsync();
        await using var scope = NewScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        await projects.CreateAsync("blog", "Blog", null);
        await Should.ThrowAsync<ProjectNameTakenException>(() => projects.CreateAsync("blog-two", "  BLOG ", null));
    }

    [Fact]
    public async Task ARename_CannotTakeTheNameOfAnotherProject_ButMayKeepItsOwnOrChangeItsLetters()
    {
        await PrepareAppsAsync();
        await CreateAsync("alpha", "Alpha");
        await CreateAsync("beta", "Beta");
        await using var scope = NewScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminProjectService>();
        var beta = (await scope.ServiceProvider.GetRequiredService<IProjectService>().FindByProjectKeyAsync("beta"))!;

        (await admin.UpdateAsync(beta.Id, new UpdateProjectRequest(" alpha ", null, true, null), null)).Result.ErrorCode.ShouldBe(Auth2ErrorCodes.ProjectNameTaken);
        (await admin.UpdateAsync(beta.Id, new UpdateProjectRequest("Beta", "new description", true, null), null)).Result.Succeeded.ShouldBeTrue();   // its own name
        var changed = await admin.UpdateAsync(beta.Id, new UpdateProjectRequest("  BETA  ", null, true, null), null);                                   // only the letters
        changed.Result.Succeeded.ShouldBeTrue();
        changed.Project!.Name.ShouldBe("BETA");   // saved without the spaces around
    }

    [Fact]
    public async Task TheDatabase_RefusesADuplicate_EvenWhenTheServicesAreSkipped()
    {
        await PrepareAppsAsync();
        await CreateAsync("first", "Unique Name");
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        db.Projects.Add(new Project { ProjectKey = "second", Name = " unique NAME ", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow });
        var ex = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());   // two requests at the same time end here
        ProjectNames.IsNameConflict(ex).ShouldBeTrue();
    }

    [Fact]
    public async Task TheMigration_RenamesExistingDuplicates_ThenCreatesTheIndex()
    {
        await PrepareAppsAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261125133105_ApplicationWebServerAndType");   // the state of an old database: no unique name yet
        var t = DateTimeOffset.UtcNow;
        db.Projects.AddRange(
            new Project { ProjectKey = "dup-a", Name = "Same Name", CreatedAtUtc = t, UpdatedAtUtc = t },
            new Project { ProjectKey = "dup-b", Name = "same name ", CreatedAtUtc = t.AddMinutes(1), UpdatedAtUtc = t },
            new Project { ProjectKey = "dup-c", Name = "SAME NAME", CreatedAtUtc = t.AddMinutes(2), UpdatedAtUtc = t });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync();   // must not fail because of the duplicates

        var names = await db.Projects.AsNoTracking().Where(p => p.ProjectKey.StartsWith("dup-")).OrderBy(p => p.CreatedAtUtc).Select(p => p.Name).ToListAsync();
        names.ShouldBe(["Same Name", "same name (2)", "SAME NAME (3)"]);   // the oldest keeps its name, the others get a number
        names.Select(ProjectNames.Key).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task TheSeed_NeverFailsBecauseSomebodyUsedItsName()
    {
        await PrepareAppsAsync();
        (await CreateAsync("mine", "Sample Project")).Result.Succeeded.ShouldBeTrue();   // a person took the name of the sample project

        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<Auth2Seeder>().EnsureSampleProjectAndApplicationsAsync();

        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        (await db.Projects.AsNoTracking().SingleAsync(p => p.ProjectKey == "mine")).Name.ShouldBe("Sample Project");                       // the person keeps theirs
        (await db.Projects.AsNoTracking().SingleAsync(p => p.ProjectKey == "sample-project")).Name.ShouldBe("Sample Project (sample-project)");   // the seed adapts
    }
}
