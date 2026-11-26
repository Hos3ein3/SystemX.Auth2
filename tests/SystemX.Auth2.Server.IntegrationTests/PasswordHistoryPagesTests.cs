using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The two pages that change the password remember the old one and refuse a password that was used before, when <c>Password.SaveOldPasswords</c> is on.</summary>
public class PasswordHistoryPagesTests : Phase6Base
{
    private const string Second = "Second-Pass-12345!";
    private const string Third = "Third-Pass-12345!";

    private async Task<Browser> SignedInAsync()
    {
        await PrepareAppsAsync(selfUi: true);
        var user = await CreateUserInAsync("appa");
        await using (var scope = NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            using var _ = scope.ServiceProvider.GetRequiredService<SystemX.Auth2.Server.Application.IGlobalScope>().Begin();
            if (!await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == A.Id)) await JoinAsync(user, A);   // with shared credentials a new user is not a member yet
        }
        var b = new Browser(Factory);
        (await b.PostPageAsync("/Account/Login?ClientId=appa", new Dictionary<string, string> { ["Input.Login"] = "sara", ["Input.Password"] = PassA })).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return b;
    }

    private static Dictionary<string, string> Change(string current, string next) => new() { ["Input.CurrentPassword"] = current, ["Input.NewPassword"] = next };

    private async Task<int> RememberedAsync()
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<SystemX.Auth2.Server.Application.IGlobalScope>().Begin();   // the table has a filter by the current project
        return await db.PasswordHistory.AsNoTracking().CountAsync();
    }

    [Theory]
    [InlineData("/Account/ChangePassword?ClientId=appa")]
    [InlineData("/Manage/ChangePassword?ClientId=appa")]
    public async Task TheOldPassword_IsRemembered_AndRefusedLater(string page)
    {
        var b = await SignedInAsync();
        await UpdateSettingsAsync(A, s => s.Password.SaveOldPasswords = true);

        (await b.PostPageAsync(page, Change(PassA, Second))).StatusCode.ShouldBe(HttpStatusCode.OK);   // changed
        (await RememberedAsync()).ShouldBe(1);                                                          // the first password was remembered

        var back = await b.PostPageAsync(page, Change(Second, PassA));                                  // the first one again
        back.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await back.Content.ReadAsStringAsync()).ShouldContain("password");
        var same = await b.PostPageAsync(page, Change(Second, Second));                                 // the current one again
        same.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await b.PostPageAsync(page, Change(Second, Third))).StatusCode.ShouldBe(HttpStatusCode.OK);    // a new one is fine
        (await RememberedAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData("/Account/ChangePassword?ClientId=appa")]
    [InlineData("/Manage/ChangePassword?ClientId=appa")]
    public async Task WithTheHistoryOff_NothingIsRemembered_AndAnOldPasswordIsAllowed(string page)
    {
        var b = await SignedInAsync();
        (await b.PostPageAsync(page, Change(PassA, Second))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RememberedAsync()).ShouldBe(0);
        (await b.PostPageAsync(page, Change(Second, PassA))).StatusCode.ShouldBe(HttpStatusCode.OK);   // the history is off: an old password may come back
    }
}

/// <summary>The same with Redis: the settings are read through the shared cache (memory + Redis, checked by version).</summary>
public class PasswordHistoryPagesWithRedisTests : PasswordHistoryPagesTests
{
    protected override bool UseRedis => true;
}

/// <summary>The same with shared credentials (one password for a person in every project: the hash lives on the user row).</summary>
public class PasswordHistoryPagesWithSharedCredentialsTests : PasswordHistoryPagesTests
{
    protected override void Customize(IServiceCollection services) =>
        services.Configure<SystemX.Auth2.Server.Application.Auth2ServerOptions>(o => o.CredentialScope = SystemX.Auth2.Contracts.Enums.CredentialScope.Shared);
}
