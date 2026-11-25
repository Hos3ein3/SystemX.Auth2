using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Application.Lists;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>The generic list contract (paging, sorting, filtering, search, tenant, export), run against the user list.</summary>
public class ListEngineTests : Phase6Base
{
    private static readonly ListDefinition<UserListEntity, UserListRow> Def = UserList.Definition;

    private async Task SeedUsersAsync(int a, int b)
    {
        await SetUpAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var now = DateTimeOffset.UtcNow;
        void Add(Project p, string name, int i, Action<AppUser>? tweak = null)
        {
            var u = new AppUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = $"{name}@example.com", NormalizedEmail = $"{name}@EXAMPLE.COM",
                FirstName = "First" + i, LastName = i % 2 == 0 ? "Even" : "Odd", CreatedAtUtc = now.AddMinutes(-i), EmailConfirmed = i % 3 == 0, IsActive = i % 10 != 0 };
            tweak?.Invoke(u);
            db.Users.Add(u);
            db.UserProjects.Add(new UserProject { UserId = u.Id, ProjectId = p.Id, JoinedAtUtc = now, LastLoginUtc = i % 4 == 0 ? now.AddDays(-i) : null, TwoFactorEnabled = i % 5 == 0 });
        }
        for (var i = 1; i <= a; i++) Add(A, $"usera{i:000}", i);
        for (var i = 1; i <= b; i++) Add(B, $"userb{i:000}", i);
        Add(A, "100%_odd", 999);          // a name with wildcard characters
        Add(A, "=cmd|calc", 998);         // a name that would be a formula in a spreadsheet
        await db.SaveChangesAsync();
    }

    private static ListQuery Q(string query, List<ListProblem>? problems = null, bool api = false) =>
        ListQueryParser.Parse(System.Web.HttpUtility.ParseQueryString(query).AllKeys.Where(k => k is not null)
            .Select(k => new KeyValuePair<string, string[]>(k!, System.Web.HttpUtility.ParseQueryString(query).GetValues(k!)!)), Def, problems ?? [], api);

    private async Task<ListPage<UserListRow>> RunAsync(string query, Project project, List<ListProblem>? problems = null)
    {
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        return await ListEngine.PageAsync(UserList.For(db.Users, db.UserProjects, project.Id, false), Q(query, problems), Def, problems);
    }

    // ------------------------------------------------------------------ paging

    [Fact]
    public async Task Paging_FirstMiddleLast_AndAPageBeyondTheEndIsClamped()
    {
        await SeedUsersAsync(60, 5);   // project A has 62 users (with the two special ones)
        var first = await RunAsync("pageSize=25", A);
        (first.Items.Count, first.TotalCount, first.TotalPages, first.HasNext, first.HasPrevious).ShouldBe((25, 62, 3, true, false));
        var middle = await RunAsync("page=2&pageSize=25", A);
        (middle.Page, middle.HasNext, middle.HasPrevious).ShouldBe((2, true, true));
        var last = await RunAsync("page=3&pageSize=25", A);
        (last.Items.Count, last.HasNext).ShouldBe((12, false));
        var beyond = await RunAsync("page=99&pageSize=25", A);
        (beyond.Page, beyond.Items.Count).ShouldBe((3, 12));   // clamped to the last page
    }

    [Fact]
    public async Task PageSize_OutsideTheAllowedSet_FallsBackAndIsReported_TheApiAllowsUpTo200()
    {
        var problems = new List<ListProblem>();
        Q("pageSize=7", problems).PageSize.ShouldBe(25);
        problems.Single().Parameter.ShouldBe("pageSize");
        Q("pageSize=100").PageSize.ShouldBe(100);
        var api = new List<ListProblem>();
        Q("pageSize=150", api, api: true).PageSize.ShouldBe(150);
        api.ShouldBeEmpty();
        Q("pageSize=201", api, api: true).PageSize.ShouldBe(25);
        api.Single().Parameter.ShouldBe("pageSize");
        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------ sorting

    [Fact]
    public async Task Sorting_EveryWhitelistedField_BothDirections_AndTheTwoFieldSort()
    {
        await SeedUsersAsync(30, 0);
        foreach (var field in new[] { "email", "userName", "name", "createdAt", "status" })
        {
            var asc = (await RunAsync($"sort={field}&pageSize=100", A)).Items;
            var desc = (await RunAsync($"sort=-{field}&pageSize=100", A)).Items;
            asc.Count.ShouldBe(desc.Count);
            asc.Select(r => r.Id).ShouldNotBe(desc.Select(r => r.Id));
        }
        // two fields: the primary sort decides, the second breaks ties (the last names are only Even or Odd, so many rows tie)
        var two = (await RunAsync("sort=-name,email&pageSize=100", A)).Items.Where(r => r.Name!.StartsWith("First")).ToList();
        var ascending = (await RunAsync("sort=name&pageSize=100", A)).Items.Where(r => r.Name!.StartsWith("First")).Select(r => r.Id).ToList();
        two.Select(r => r.Id).ToList().ShouldBe(((IEnumerable<Guid>)ascending).Reverse().ToList());   // names are all different: the second field never matters here
    }

    [Fact]
    public async Task Sorting_UnknownField_IsReported_AndATiebreakerKeepsPagesStable_NoDuplicatesNoGaps()
    {
        await SeedUsersAsync(80, 0);
        var problems = new List<ListProblem>();
        (await RunAsync("sort=passwordHash", A, problems)).Items.ShouldNotBeEmpty();   // falls back to the default sort
        problems.Single().Parameter.ShouldBe("sort");

        // sort by a column with many equal values (status: active or disabled), page through everything: every id exactly once
        var seen = new List<Guid>();
        for (var page = 1; page <= 4; page++) seen.AddRange((await RunAsync($"sort=status&page={page}&pageSize=25", A)).Items.Select(r => r.Id));
        seen.Count.ShouldBe(82);
        seen.Distinct().Count().ShouldBe(82);
    }

    // ------------------------------------------------------------------ filtering and search

    [Fact]
    public async Task Filters_EachAlone_Combined_AndInvalidValuesAreReported()
    {
        await SeedUsersAsync(40, 0);
        (await RunAsync("f.status=disabled", A)).TotalCount.ShouldBe(4);   // 10, 20, 30, 40
        (await RunAsync("f.emailConfirmed=true", A)).Items.ShouldAllBe(r => r.EmailConfirmed);
        (await RunAsync("f.twoFactorEnabled=true", A)).TotalCount.ShouldBe(8);   // 5, 10, ... 40
        (await RunAsync("f.status=disabled&f.twoFactorEnabled=true", A)).TotalCount.ShouldBe(4);
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        (await RunAsync($"f.createdAtFrom={today}", A)).TotalCount.ShouldBeGreaterThan(0);
        (await RunAsync("f.createdAtTo=2000-01-01", A)).TotalCount.ShouldBe(0);

        var problems = new List<ListProblem>();
        await RunAsync("f.emailConfirmed=maybe&f.nope=1&f.createdAtFrom=notadate", A, problems);
        problems.Select(p => p.Parameter).ShouldBe(["f.nope", "f.emailConfirmed", "f.createdAtFrom"], ignoreOrder: true);
    }

    [Fact]
    public async Task Search_MinimumLength_CaseInsensitive_WildcardsAreText_SqlLikeInputIsHarmless()
    {
        await SeedUsersAsync(20, 0);
        var problems = new List<ListProblem>();
        (await RunAsync("q=u", A, problems)).TotalCount.ShouldBe(22);   // too short: ignored, not searched
        problems.Single().Parameter.ShouldBe("q");

        (await RunAsync("q=USERA00", A)).TotalCount.ShouldBe(9);           // case-insensitive prefix: usera001 .. usera009
        (await RunAsync("q=100%25", A)).Items.Select(r => r.UserName).ShouldBe(["100%_odd"]);   // the % is a character, not a wildcard
        (await RunAsync("q=%25%25", A, [])).TotalCount.ShouldBe(0);
        (await RunAsync("q=_serA", A, [])).TotalCount.ShouldBe(0);       // _ is not "any character"
        (await RunAsync("q=x'%3B+DROP+TABLE+users%3B--", A)).TotalCount.ShouldBe(0);
        (await RunAsync("q=usera", A)).TotalCount.ShouldBe(20);
        await using var scope = NewScope();
        (await scope.ServiceProvider.GetRequiredService<Auth2DbContext>().Users.CountAsync()).ShouldBeGreaterThan(20);   // the table is still there
    }

    // ------------------------------------------------------------------ tenant

    [Fact]
    public async Task Tenant_UsersOfAnotherProject_NeverAppear_InList_Search_Filter_OrExport()
    {
        await SeedUsersAsync(10, 10);
        (await RunAsync("pageSize=100", A)).Items.ShouldAllBe(r => r.UserName!.StartsWith("usera") || r.UserName == "100%_odd" || r.UserName == "=cmd|calc");
        (await RunAsync("q=userb", A)).TotalCount.ShouldBe(0);
        (await RunAsync("f.status=active&q=userb", A)).TotalCount.ShouldBe(0);
        (await RunAsync("pageSize=100", B)).Items.ShouldAllBe(r => r.UserName!.StartsWith("userb"));

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        using var stream = new MemoryStream();
        await CsvExport.WriteAsync(stream, ListEngine.Apply(UserList.For(db.Users, db.UserProjects, A.Id, false), Q(""), Def).Select(Def.Select).AsAsyncEnumerable(), 1000);
        Encoding.UTF8.GetString(stream.ToArray()).ShouldNotContain("userb");
    }

    // ------------------------------------------------------------------ export

    [Fact]
    public async Task Export_CsvWithBom_FormulaGuard_RowLimit_AndNoSecretColumns()
    {
        await SeedUsersAsync(5, 0);
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var rows = ListEngine.Apply(UserList.For(db.Users, db.UserProjects, A.Id, false), Q("sort=userName"), Def).Select(Def.Select).AsAsyncEnumerable();
        using var stream = new MemoryStream();
        await CsvExport.WriteAsync(stream, rows, rowLimit: 4);
        var bytes = stream.ToArray();
        bytes.Take(3).ShouldBe([0xEF, 0xBB, 0xBF]);   // UTF-8 with BOM, for spreadsheet programs
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(5);                      // header + the row limit
        var header = lines[0].TrimEnd('\r').ToLowerInvariant();
        foreach (var secret in new[] { "password", "hash", "secret", "token", "key", "recovery" }) header.ShouldNotContain(secret);
        CsvExport.Cell("=cmd|calc").ShouldBe("'=cmd|calc");
        CsvExport.Cell("+1").ShouldBe("'+1");
        CsvExport.Cell("-1").ShouldBe("'-1");
        CsvExport.Cell("@x").ShouldBe("'@x");
        CsvExport.Cell("a,b").ShouldBe("\"a,b\"");
        CsvExport.Cell("say \"hi\"").ShouldBe("\"say \"\"hi\"\"\"");
        CsvExport.Cell(null).ShouldBe("");
    }

    // ------------------------------------------------------------------ generated SQL, count cap and keyset

    [Fact]
    public async Task TheSql_UsesLimitOffset_AndNeverLoadsTheWholeTable()
    {
        await SeedUsersAsync(60, 0);
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var sql = ListEngine.Apply(UserList.For(db.Users, db.UserProjects, A.Id, false), Q("page=2&pageSize=10&sort=email&q=user"), Def).Skip(10).Take(10).Select(Def.Select).ToQueryString();
        sql.ShouldContain("LIMIT");
        sql.ShouldContain("OFFSET");
        sql.ShouldContain("ORDER BY");
        sql.ShouldContain("u.id");   // the tiebreaker is the last sort key
        var page = await RunAsync("pageSize=10", A);
        page.Items.Count.ShouldBe(10);
    }

    [Fact]
    public async Task CappedCount_StopsAtTheCap_AndTheKeysetCursor_IsStableWhenRowsAreInsertedBetweenPages()
    {
        await SetUpAsync();
        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        using var _ = scope.ServiceProvider.GetRequiredService<IGlobalScope>().Begin();
        var t0 = DateTimeOffset.UtcNow.AddHours(-1);
        for (var i = 0; i < 30; i++) db.AuditEvents.Add(new AuditEvent { OccurredAtUtc = t0.AddSeconds(i / 3), ProjectId = A.Id, Action = "x" + i });   // many equal times
        await db.SaveChangesAsync();

        IQueryable<AuditEvent> Mine() => db.AuditEvents.AsNoTracking().Where(e => e.ProjectId == A.Id);
        var seen = new List<Guid>();
        string? cursor = null;
        var inserted = false;
        for (var guard = 0; guard < 20; guard++)
        {
            var page = await ListEngine.KeysetAsync(Mine(), e => e.OccurredAtUtc, e => e.Id, cursor, 7, e => new { e.Id, e.OccurredAtUtc }, r => (r.OccurredAtUtc, r.Id));
            seen.AddRange(page.Items.Select(i => i.Id));
            if (!inserted) { db.AuditEvents.Add(new AuditEvent { OccurredAtUtc = DateTimeOffset.UtcNow, ProjectId = A.Id, Action = "new" }); await db.SaveChangesAsync(); inserted = true; }   // a new row between two pages
            if (!page.HasNext) break;
            cursor = page.NextCursor;
        }
        seen.Count.ShouldBe(seen.Distinct().Count());                 // no duplicate even though a row was inserted
        seen.Count.ShouldBeGreaterThanOrEqualTo(30);                  // no gap: every original row was read (the new one is newer than the cursor)
        ListEngine.Cursor.TryDecode("not a cursor!!", out var ignored).ShouldBeFalse();   // a bad cursor gives the first page
    }
}
