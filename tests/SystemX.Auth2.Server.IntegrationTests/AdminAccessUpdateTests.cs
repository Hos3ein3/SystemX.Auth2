using System.Net;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Admin;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>Change one role, permission, group or scope through the admin API, read one by id, and the ids that "create" now returns.</summary>
public class AdminAccessUpdateTests : AdminBase
{
    [Fact]
    public async Task Create_ReturnsTheNewItemWithItsId()
    {
        await StartAsync();
        var role = await SendAsync(HttpMethod.Post, "/roles", ProjectToken, new CreateRoleRequest("Editor", "Edits"));
        role.Status.ShouldBe(HttpStatusCode.Created);
        role.Body.Str("name").ShouldBe("Editor");
        role.Body.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);

        var permission = await SendAsync(HttpMethod.Post, "/permissions", ProjectToken, new PermissionDefinition("Orders.Read", "Read orders"));
        permission.Status.ShouldBe(HttpStatusCode.Created);
        permission.Body.Str("key").ShouldBe("orders.read");
        permission.Body.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);

        var group = await SendAsync(HttpMethod.Post, "/groups", ProjectToken, new CreateGroupRequest("Finance", "Finance team"));
        group.Status.ShouldBe(HttpStatusCode.Created);
        group.Body.Str("name").ShouldBe("finance");
        group.Body.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);

        (await GetA($"/roles/{role.Body.GetProperty("id").GetGuid()}", ProjectToken)).Body.Str("description").ShouldBe("Edits");
        (await GetA($"/groups/{Guid.NewGuid()}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Role_DescriptionChanges_TheNameNeverDoes_SystemRolesAreRefused()
    {
        await StartAsync();
        var id = (await SendAsync(HttpMethod.Post, "/roles", ProjectToken, new CreateRoleRequest("Editor", "Old"))).Body.GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Put, $"/roles/{id}", ProjectToken, new UpdateRoleRequest(Description: "New"))).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/roles/{id}", ProjectToken)).Body.Str("description").ShouldBe("New");
        (await SendAsync(HttpMethod.Put, $"/roles/{id}", ProjectToken, new UpdateRoleRequest(Name: "Editor", Description: ""))).Status.ShouldBe(HttpStatusCode.NoContent);   // the same name is fine; an empty text clears
        (await GetA($"/roles/{id}", ProjectToken)).Body.TryGetProperty("description", out var d).ShouldBeTrue();
        d.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        (await SendAsync(HttpMethod.Put, $"/roles/{id}", ProjectToken, new UpdateRoleRequest(Name: "Other"))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await GetA($"/roles/{id}", ProjectToken)).Body.Str("name").ShouldBe("Editor");

        var projectAdminRole = (await GetA("/roles", ProjectToken)).Body.GetProperty("items").EnumerateArray().Single(r => r.Str("name") == "ProjectAdmin").GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Put, $"/roles/{projectAdminRole}", ProjectToken, new UpdateRoleRequest(Description: "x"))).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"/roles/{Guid.NewGuid()}", ProjectToken, new UpdateRoleRequest(Description: "x"))).Status.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/roles/{id}", PlainToken, new UpdateRoleRequest(Description: "x"))).Status.ShouldBe(HttpStatusCode.Forbidden);   // not an admin
    }

    [Fact]
    public async Task Permission_TextsChange_TheKeyNeverDoes_NullKeepsTheOldText()
    {
        await StartAsync();
        var id = (await SendAsync(HttpMethod.Post, "/permissions", ProjectToken, new PermissionDefinition("orders.read", "Read", "Old", "Orders"))).Body.GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Put, $"/permissions/{id}", ProjectToken, new UpdatePermissionRequest(DisplayName: "Read orders", Description: "New"))).Status.ShouldBe(HttpStatusCode.NoContent);
        var one = (await GetA($"/permissions/{id}", ProjectToken)).Body;
        one.Str("key").ShouldBe("orders.read");
        one.Str("displayName").ShouldBe("Read orders");
        one.Str("description").ShouldBe("New");
        one.Str("group").ShouldBe("Orders");   // null kept it

        (await SendAsync(HttpMethod.Put, $"/permissions/{id}", ProjectToken, new UpdatePermissionRequest(Group: ""))).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/permissions/{id}", ProjectToken)).Body.GetProperty("group").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await SendAsync(HttpMethod.Put, $"/permissions/{Guid.NewGuid()}", ProjectToken, new UpdatePermissionRequest(Description: "x"))).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Group_TextsChange_AndOneGroupShowsItsLinks()
    {
        await StartAsync();
        var id = (await SendAsync(HttpMethod.Post, "/groups", ProjectToken, new CreateGroupRequest("finance", "Fin", "Old"))).Body.GetProperty("id").GetGuid();
        var roleId = (await SendAsync(HttpMethod.Post, "/roles", ProjectToken, new CreateRoleRequest("Editor"))).Body.GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Put, $"/groups/{id}/roles/{roleId}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NoContent);

        (await SendAsync(HttpMethod.Put, $"/groups/{id}", ProjectToken, new UpdateGroupRequest(DisplayName: "Finance team"))).Status.ShouldBe(HttpStatusCode.NoContent);
        var one = (await GetA($"/groups/{id}", ProjectToken)).Body;
        one.Str("displayName").ShouldBe("Finance team");
        one.Str("description").ShouldBe("Old");
        one.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["Editor"]);
        one.GetProperty("memberCount").GetInt32().ShouldBe(0);
        (await SendAsync(HttpMethod.Put, $"/groups/{Guid.NewGuid()}", ProjectToken, new UpdateGroupRequest(DisplayName: "x"))).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Scopes_AnAdminReads_OnlyAGlobalAdminChanges()
    {
        await StartAsync();
        (await GetA("/scopes", ProjectToken)).Status.ShouldBe(HttpStatusCode.OK);
        (await GetA("/scopes", PlainToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/scopes", ProjectToken, new CreateScopeRequest("reports.read"))).Status.ShouldBe(HttpStatusCode.Forbidden);

        var created = await SendAsync(HttpMethod.Post, "/scopes", GlobalToken, new CreateScopeRequest("reports.read", "Reports", "Read reports", ["api://reports"]));
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body.ToString());
        var id = created.Body.GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Post, "/scopes", GlobalToken, new CreateScopeRequest("reports.read"))).Status.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Post, "/scopes", GlobalToken, new CreateScopeRequest("has space"))).Status.ShouldBe(HttpStatusCode.BadRequest);

        var one = (await GetA($"/scopes/{id}", ProjectToken)).Body;
        one.Str("name").ShouldBe("reports.read");
        one.GetProperty("resources").EnumerateArray().Select(r => r.GetString()).ShouldBe(["api://reports"]);
        (await GetA("/scopes?q=reports", ProjectToken)).Body.GetProperty("items").EnumerateArray().Select(r => r.Str("name")).ShouldContain("reports.read");

        (await SendAsync(HttpMethod.Put, $"/scopes/{id}", ProjectToken, new UpdateScopeRequest(DisplayName: "x"))).Status.ShouldBe(HttpStatusCode.Forbidden);
        var updated = await SendAsync(HttpMethod.Put, $"/scopes/{id}", GlobalToken, new UpdateScopeRequest(DisplayName: "Reports 2"));
        updated.Status.ShouldBe(HttpStatusCode.OK);
        updated.Body.Str("displayName").ShouldBe("Reports 2");
        updated.Body.Str("description").ShouldBe("Read reports");   // null kept it

        (await SendAsync(HttpMethod.Delete, $"/scopes/{id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/scopes/{id}", GlobalToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA($"/scopes/{id}", ProjectToken)).Status.ShouldBe(HttpStatusCode.NotFound);
    }
}
