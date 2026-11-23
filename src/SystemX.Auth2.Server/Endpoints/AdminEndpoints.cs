using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Admin;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Endpoints;

/// <summary>Who may call an admin route.</summary>
internal enum AdminLevel
{
    /// <summary>A GlobalAdmin, or a ProjectAdmin of the application the call works on.</summary>
    Project,
    /// <summary>Only a GlobalAdmin (scope <c>auth2.admin.global</c> as well).</summary>
    Global,
}

/// <summary>
/// Guards the admin API (policies <c>RequireProjectAdmin</c> and <c>RequireGlobalAdmin</c>, in code).
/// It needs the token of a signed-in user with scope <c>auth2.admin</c> (and <c>auth2.admin.global</c> for global routes).
/// The caller is found in their <b>home</b> application (the audience of the token). A global admin may work on another application
/// with the header <c>X-Auth2-Application: {projectKey}</c>; a project admin never can.
/// </summary>
internal sealed class AdminApiFilter(AdminLevel level) : IEndpointFilter
{
    public const string ApplicationHeader = "X-Auth2-Application";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var auth = await http.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (!auth.Succeeded || auth.Principal is null)
        {
            http.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return ApiResults.Error(http, StatusCodes.Status401Unauthorized, Auth2ErrorCodes.InvalidToken);
        }
        var principal = auth.Principal;
        http.User = principal;
        if (principal.GetClaim(Auth2Claims.Machine) == "true") return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
        if (!principal.HasScope(Auth2Scopes.Admin) || (level == AdminLevel.Global && !principal.HasScope(Auth2Scopes.AdminGlobal)))
            return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.InsufficientScope);

        var options = http.RequestServices.GetRequiredService<IOptions<Auth2ServerOptions>>().Value;
        var single = options.Mode == ServerMode.SingleApplication;

        // Who is calling? Found in a separate scope, so the request scope is still free to work on the target application.
        Auth2Application home; AppUser admin; bool isGlobal, isProjectAdmin;
        await using (var child = http.RequestServices.CreateAsyncScope())
        {
            var sp = child.ServiceProvider;
            sp.GetRequiredService<IClientIdAccessor>().ClientId = principal.GetAudiences().FirstOrDefault();
            try { home = await sp.GetRequiredService<IAuthFlowResolver>().GetApplicationAsync(http.RequestAborted); }
            catch (ApplicationNotFoundException) { return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.ApplicationNotFound); }
            if (!home.IsEnabled) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.ApplicationDisabled);

            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var found = await users.FindByIdAsync(principal.GetClaim(Claims.Subject) ?? "");
            if (found is null || !await sp.GetRequiredService<ILoginService>().IsSubjectValidAsync(found, http.RequestAborted))
                return ApiResults.Error(http, StatusCodes.Status401Unauthorized, Auth2ErrorCodes.InvalidToken);
            admin = found;
            // GlobalAdmin is a global role: it counts only in the _global project (or the single application).
            isGlobal = (home.IsGlobalProject || single) && await users.IsInRoleAsync(admin, Auth2Roles.GlobalAdmin);
            isProjectAdmin = await users.IsInRoleAsync(admin, Auth2Roles.ProjectAdmin);
        }
        if (level == AdminLevel.Global ? !isGlobal : !(isGlobal || isProjectAdmin))
            return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);

        // Which application does the call work on?
        var target = home;
        var requested = http.Request.Headers[ApplicationHeader].FirstOrDefault();
        if (!single && !string.IsNullOrWhiteSpace(requested) && !string.Equals(requested.Trim(), home.ProjectKey, StringComparison.OrdinalIgnoreCase))
        {
            if (!isGlobal) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
            var found = await http.RequestServices.GetRequiredService<IApplicationService>().FindByProjectKeyAsync(requested.Trim().ToLowerInvariant(), http.RequestAborted);
            if (found is null) return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.ApplicationNotFound);
            target = found;
        }

        // The request scope now works on the target application.
        http.RequestServices.GetRequiredService<IApplicationSelection>().ProjectKey = single ? null : target.ProjectKey;
        http.RequestServices.GetRequiredService<IClientIdAccessor>().ClientId = target.ClientId;
        await http.RequestServices.GetRequiredService<IApplicationContext>().GetRequiredAsync(http.RequestAborted);

        http.Items[AdminItems.Context] = new AdminContext(admin.Id, isGlobal, home, target);
        http.Items[AdminItems.Actor] = admin.Id;
        return await next(context);
    }
}

/// <summary>The admin JSON API under <c>/api/auth2/admin</c>.</summary>
public static class AdminEndpoints
{
    private static AdminContext Ctx(this HttpContext http) => (AdminContext)http.Items[AdminItems.Context]!;

    private static RouteHandlerBuilder Require(this RouteHandlerBuilder b, AdminLevel level) => b.AddEndpointFilter(new AdminApiFilter(level));

    private static PageQuery PageOf(HttpRequest r) => new(
        int.TryParse(r.Query["page"], out var p) ? p : 1,
        int.TryParse(r.Query["pageSize"], out var s) ? s : 50,
        r.Query["q"].FirstOrDefault(), r.Query["sort"].FirstOrDefault(), string.Equals(r.Query["desc"], "true", StringComparison.OrdinalIgnoreCase));

    private static IResult Created(object body) => Results.Json(body, statusCode: StatusCodes.Status201Created);

    /// <summary>Maps every admin route.</summary>
    public static IEndpointRouteBuilder MapAuth2Admin(this IEndpointRouteBuilder routes)
    {
        var g = routes.MapGroup(Auth2Routes.Admin);
        MapApplications(g);
        MapUsers(g);
        MapAccess(g);

        // Keys (global admin only). Rotation: new keys sign from now on, old keys keep checking old tokens until they are removed.
        g.MapGet("/keys", async (HttpContext http, IKeyRing keys) => Results.Ok(await keys.ListAsync(http.RequestAborted))).Require(AdminLevel.Global);
        g.MapPost("/keys/rotate", async (HttpContext http, IKeyRing keys, IAuditService audit) =>
        {
            var list = await keys.RotateAsync(http.RequestAborted);
            await audit.WriteAsync(new AuditEntry("keys.rotated", null, TargetType: "keys"), http.RequestAborted);
            return Results.Ok(list);
        }).Require(AdminLevel.Global);

        g.MapGet("/audit-events", async (HttpContext http, IAuditReader reader, string? action, Guid? actorUserId, string? targetType, DateTimeOffset? from, DateTimeOffset? to, bool? all) =>
        {
            if (all == true && !http.Ctx().IsGlobalAdmin) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
            return Results.Ok(await reader.QueryAsync(PageOf(http.Request), action, actorUserId, targetType, from, to, all == true, http.RequestAborted));
        }).Require(AdminLevel.Project);

        return routes;
    }

    // ------------------------------------------------------------------ applications

    private static void MapApplications(RouteGroupBuilder g)
    {
        g.MapGet("/applications", async (HttpContext http, IAdminApplicationService apps) =>
        {
            if (http.Ctx().IsGlobalAdmin) return Results.Ok(await apps.ListAsync(PageOf(http.Request), http.RequestAborted));
            var own = await apps.GetAsync(http.Ctx().Home.Id, http.RequestAborted);   // a project admin sees only the own application
            return Results.Ok(new PagedResult<ApplicationDto>(own is null ? [] : [own], 1, 50, own is null ? 0 : 1));
        }).Require(AdminLevel.Project);

        g.MapGet("/applications/{id:guid}", async (HttpContext http, Guid id, IAdminApplicationService apps) =>
            !Allowed(http, id) ? Hidden(http) : await apps.GetAsync(id, http.RequestAborted) is { } app ? Results.Ok(app) : Hidden(http)).Require(AdminLevel.Project);

        g.MapPost("/applications", async (HttpContext http, CreateApplicationRequest r, IAdminApplicationService apps) =>
        {
            var (result, response) = await apps.CreateAsync(r, http.RequestAborted);
            return result.Succeeded ? Created(response!) : ApiResults.From(http, result);
        }).Require(AdminLevel.Global);

        g.MapPut("/applications/{id:guid}", async (HttpContext http, Guid id, UpdateApplicationRequest r, IAdminApplicationService apps) =>
        {
            var (result, app) = await apps.UpdateAsync(id, r, http.RequestAborted);
            return result.Succeeded ? Results.Ok(app) : ApiResults.From(http, result);
        }).Require(AdminLevel.Global);

        g.MapDelete("/applications/{id:guid}", async (HttpContext http, Guid id, IAdminApplicationService apps) =>
            ApiResults.From(http, await apps.DeleteAsync(id, http.RequestAborted))).Require(AdminLevel.Global);

        g.MapGet("/applications/{id:guid}/settings", async (HttpContext http, Guid id, IAdminApplicationService apps) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            if (await apps.GetSettingsAsync(id, http.RequestAborted) is not { } s) return Hidden(http);
            http.Response.Headers.ETag = $"\"v{s.Version}\"";
            return Results.Content(SettingsJson.Serialize(s.Settings), "application/json");
        }).Require(AdminLevel.Project);

        g.MapPut("/applications/{id:guid}/settings", async (HttpContext http, Guid id, IAdminApplicationService apps) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            Domain.Settings.ApplicationSettings settings;
            try
            {
                using var reader = new StreamReader(http.Request.Body);
                settings = SettingsJson.Deserialize(await reader.ReadToEndAsync(http.RequestAborted));
            }
            catch (JsonException) { return ApiResults.Error(http, StatusCodes.Status400BadRequest, Auth2ErrorCodes.ValidationFailed); }
            var (result, version) = await apps.UpdateSettingsAsync(id, settings, http.Ctx().IsGlobalAdmin, ParseVersion(http.Request.Headers.IfMatch.FirstOrDefault()), http.RequestAborted);
            if (!result.Succeeded) return ApiResults.From(http, result);
            http.Response.Headers.ETag = $"\"v{version}\"";
            return Results.NoContent();
        }).Require(AdminLevel.Project);

        g.MapPost("/applications/{id:guid}/secret/rotate", async (HttpContext http, Guid id, IAdminApplicationService apps) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, secret) = await apps.RotateSecretAsync(id, http.Ctx().IsGlobalAdmin, http.RequestAborted);
            return result.Succeeded ? Results.Ok(new RotateSecretResponse(secret!)) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);

        g.MapGet("/applications/{id:guid}/redirect-uris", async (HttpContext http, Guid id, IAdminApplicationService apps) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            if (await apps.GetSettingsAsync(id, http.RequestAborted) is not { } s) return Hidden(http);
            return Results.Ok(new RedirectUrisDto(s.Settings.Redirects.AllowedRedirectUris, s.Settings.Redirects.AllowedPostLogoutRedirectUris));
        }).Require(AdminLevel.Project);

        g.MapPut("/applications/{id:guid}/redirect-uris", async (HttpContext http, Guid id, RedirectUrisDto r, IAdminApplicationService apps) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            if (await apps.GetSettingsAsync(id, http.RequestAborted) is not { } s) return Hidden(http);
            var next = SettingsJson.Clone(s.Settings);
            next.Redirects.AllowedRedirectUris = [.. r.RedirectUris];
            next.Redirects.AllowedPostLogoutRedirectUris = [.. r.PostLogoutRedirectUris];
            var (result, _) = await apps.UpdateSettingsAsync(id, next, http.Ctx().IsGlobalAdmin, ParseVersion(http.Request.Headers.IfMatch.FirstOrDefault()), http.RequestAborted);
            return ApiResults.From(http, result);
        }).Require(AdminLevel.Project);
    }

    /// <summary>A global admin may use any application id. A project admin only the own one.</summary>
    private static bool Allowed(HttpContext http, Guid id) => http.Ctx().IsGlobalAdmin || id == http.Ctx().Home.Id;

    /// <summary>Other applications do not show up for a project admin: 404, not 403.</summary>
    private static IResult Hidden(HttpContext http) => ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.NotFound);

    private static int? ParseVersion(string? ifMatch)
    {
        var v = ifMatch?.Trim().Trim('"');
        return v is { Length: > 1 } && v[0] == 'v' && int.TryParse(v[1..], out var n) ? n : null;
    }

    // ------------------------------------------------------------------ users

    private static void MapUsers(RouteGroupBuilder g)
    {
        g.MapGet("/users", async (HttpContext http, IAdminUserService users, bool? lockedOut) =>
            Results.Ok(await users.ListAsync(PageOf(http.Request), lockedOut, http.RequestAborted))).Require(AdminLevel.Project);

        g.MapGet("/users/{id:guid}", async (HttpContext http, Guid id, IAdminUserService users) =>
        {
            if (await users.GetAsync(id, http.RequestAborted) is not { } view) return Hidden(http);
            http.Response.Headers.ETag = view.ETag;
            return Results.Ok(view.User);
        }).Require(AdminLevel.Project);

        g.MapPost("/users", async (HttpContext http, AdminCreateUserRequest r, IAdminUserService users) =>
        {
            var (result, response) = await users.CreateAsync(r, http.RequestAborted);
            return result.Succeeded ? Created(response!) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);

        g.MapPut("/users/{id:guid}", async (HttpContext http, Guid id, AdminUpdateUserRequest r, IAdminUserService users) =>
        {
            var (result, view) = await users.UpdateAsync(id, r, http.Request.Headers.IfMatch.FirstOrDefault(), http.RequestAborted);
            if (!result.Succeeded) return ApiResults.From(http, result);
            http.Response.Headers.ETag = view!.ETag;
            return Results.Ok(view.User);
        }).Require(AdminLevel.Project);

        g.MapDelete("/users/{id:guid}", async (HttpContext http, Guid id, IAdminUserService users) =>
            ApiResults.From(http, await users.RemoveAsync(id, http.RequestAborted))).Require(AdminLevel.Project);

        g.MapPost("/users/{id:guid}/lock", async (HttpContext http, Guid id, IAdminUserService users, LockUserRequest? r) =>
            ApiResults.From(http, await users.LockAsync(id, r?.UntilUtc, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapPost("/users/{id:guid}/unlock", async (HttpContext http, Guid id, IAdminUserService users) =>
            ApiResults.From(http, await users.UnlockAsync(id, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapPost("/users/{id:guid}/reset-2fa", async (HttpContext http, Guid id, IAdminUserService users) =>
            ApiResults.From(http, await users.ResetTwoFactorAsync(id, http.RequestAborted))).Require(AdminLevel.Project);

        g.MapGet("/users/{id:guid}/sessions", async (HttpContext http, Guid id, IAdminUserService users) =>
        {
            var (result, list) = await users.ListSessionsAsync(id, http.RequestAborted);
            return result.Succeeded ? Results.Ok(list) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);
        g.MapDelete("/users/{id:guid}/sessions", async (HttpContext http, Guid id, IAdminUserService users) =>
            ApiResults.From(http, await users.RevokeSessionsAsync(id, null, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapDelete("/users/{id:guid}/sessions/{sessionId}", async (HttpContext http, Guid id, string sessionId, IAdminUserService users) =>
            ApiResults.From(http, await users.RevokeSessionsAsync(id, sessionId, http.RequestAborted))).Require(AdminLevel.Project);

        // the access of one user: roles, permissions, groups
        g.MapGet("/users/{id:guid}/access", async (HttpContext http, Guid id, UserManager<AppUser> manager, IAdminUserService users, IAccessService access) =>
        {
            if (await users.GetAsync(id, http.RequestAborted) is null) return Hidden(http);
            return Results.Ok(await access.GetCurrentUserAsync((await manager.FindByIdAsync(id.ToString("D")))!, [], http.RequestAborted));
        }).Require(AdminLevel.Project);

        g.MapPut("/users/{id:guid}/roles/{roleId:guid}", async (HttpContext http, Guid id, Guid roleId, Q q) =>
        {
            if (await q.UserAsync(id, http) is not { } user) return Hidden(http);
            if (await q.RoleNameAsync(roleId) is not { } name) return Hidden(http);
            if (name == Auth2Roles.GlobalAdmin && !http.Ctx().IsGlobalAdmin) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
            return ApiResults.From(http, await q.Access.AssignRoleAsync(user, name, http.RequestAborted));
        }).Require(AdminLevel.Project);
        g.MapDelete("/users/{id:guid}/roles/{roleId:guid}", async (HttpContext http, Guid id, Guid roleId, Q q) =>
        {
            if (await q.UserAsync(id, http) is not { } user) return Hidden(http);
            if (await q.RoleNameAsync(roleId) is not { } name) return Hidden(http);
            if (name == Auth2Roles.GlobalAdmin && !http.Ctx().IsGlobalAdmin) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
            if (user.Id == http.Ctx().UserId && name is Auth2Roles.GlobalAdmin or Auth2Roles.ProjectAdmin) return ApiResults.Error(http, StatusCodes.Status409Conflict, Auth2ErrorCodes.Conflict);   // not your own admin role
            return ApiResults.From(http, await q.Access.RemoveRoleAsync(user, name, http.RequestAborted));
        }).Require(AdminLevel.Project);

        // Makes the user a project admin of the application the call works on. The role is created the first time.
        g.MapPut("/users/{id:guid}/project-admin", async (HttpContext http, Guid id, Q q) =>
        {
            if (await q.UserAsync(id, http) is not { } user) return Hidden(http);
            var app = http.Ctx().Target;
            if (await q.Db.Roles.AnyAsync(r => r.ApplicationId == app.Id && r.NormalizedName == Auth2Roles.ProjectAdmin.ToUpperInvariant(), http.RequestAborted) == false)
            {
                var created = await q.Roles.CreateAsync(new AppRole(Auth2Roles.ProjectAdmin) { ApplicationId = app.Id, IsSystem = true, Description = "Manages this application." });
                if (!created.Succeeded) return ApiResults.Error(http, StatusCodes.Status400BadRequest, Auth2ErrorCodes.ValidationFailed, created.Errors);
            }
            return ApiResults.From(http, await q.Access.AssignRoleAsync(user, Auth2Roles.ProjectAdmin, http.RequestAborted));
        }).Require(AdminLevel.Project);

        g.MapPut("/users/{id:guid}/permissions/{key}", async (HttpContext http, Guid id, string key, Q q) =>
            await q.UserAsync(id, http) is not { } user ? Hidden(http)
                : ApiResults.From(http, await q.Access.GrantPermissionAsync(user, key, http.Ctx().UserId, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapDelete("/users/{id:guid}/permissions/{key}", async (HttpContext http, Guid id, string key, Q q) =>
            await q.UserAsync(id, http) is not { } user ? Hidden(http)
                : ApiResults.From(http, await q.Access.RevokePermissionAsync(user, key, http.RequestAborted))).Require(AdminLevel.Project);

        g.MapPut("/users/{id:guid}/groups/{groupId:guid}", async (HttpContext http, Guid id, Guid groupId, Q q) =>
            await q.UserAsync(id, http) is not { } user || await q.GroupNameAsync(groupId) is not { } name ? Hidden(http)
                : ApiResults.From(http, await q.Groups.AddUserAsync(name, user, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapDelete("/users/{id:guid}/groups/{groupId:guid}", async (HttpContext http, Guid id, Guid groupId, Q q) =>
            await q.UserAsync(id, http) is not { } user || await q.GroupNameAsync(groupId) is not { } name ? Hidden(http)
                : ApiResults.From(http, await q.Groups.RemoveUserAsync(name, user, http.RequestAborted))).Require(AdminLevel.Project);
    }

    // ------------------------------------------------------------------ roles, permissions, groups

    private static void MapAccess(RouteGroupBuilder g)
    {
        g.MapGet("/roles", async (HttpContext http, Q q) => Results.Ok(await q.RolesAsync(PageOf(http.Request), http.RequestAborted))).Require(AdminLevel.Project);
        g.MapPost("/roles", async (HttpContext http, CreateRoleRequest r, Q q) =>
        {
            if (string.Equals(r.Name?.Trim(), Auth2Roles.GlobalAdmin, StringComparison.OrdinalIgnoreCase)) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
            var result = await q.Access.CreateRoleAsync(r.Name ?? "", r.Description, http.RequestAborted);
            return result.Succeeded ? Created(new { name = r.Name!.Trim() }) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);
        g.MapDelete("/roles/{id:guid}", async (HttpContext http, Guid id, Q q) =>
            await q.RoleNameAsync(id) is not { } name ? Hidden(http) : ApiResults.From(http, await q.Access.DeleteRoleAsync(name, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapGet("/roles/{id:guid}/permissions", async (HttpContext http, Guid id, Q q) =>
            await q.RoleNameAsync(id) is not { } name ? Hidden(http)
                : Results.Ok((await q.Access.ListRolesAsync(http.RequestAborted)).First(r => r.Name == name).Permissions)).Require(AdminLevel.Project);
        g.MapPut("/roles/{id:guid}/permissions/{key}", async (HttpContext http, Guid id, string key, Q q) =>
            await q.RoleNameAsync(id) is not { } name ? Hidden(http) : ApiResults.From(http, await q.Access.AddRolePermissionAsync(name, key, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapDelete("/roles/{id:guid}/permissions/{key}", async (HttpContext http, Guid id, string key, Q q) =>
            await q.RoleNameAsync(id) is not { } name ? Hidden(http) : ApiResults.From(http, await q.Access.RemoveRolePermissionAsync(name, key, http.RequestAborted))).Require(AdminLevel.Project);

        g.MapGet("/permissions", async (HttpContext http, Q q) => Results.Ok(await q.PermissionsAsync(PageOf(http.Request), http.RequestAborted))).Require(AdminLevel.Project);
        g.MapPost("/permissions", async (HttpContext http, PermissionDefinition r, Q q) =>
        {
            var result = await q.Access.CreatePermissionAsync(r, http.RequestAborted);
            return result.Succeeded ? Created(new { key = AccessKeys.NormalizePermissionKey(r.Key) }) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);
        g.MapPut("/permissions/{id:guid}/obsolete", async (HttpContext http, Guid id, SetObsoleteRequest r, Q q) =>
            await q.PermissionKeyAsync(id) is not { } key ? Hidden(http) : ApiResults.From(http, await q.Access.SetPermissionObsoleteAsync(key, r.IsObsolete, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapDelete("/permissions/{id:guid}", async (HttpContext http, Guid id, Q q) =>
            await q.PermissionKeyAsync(id) is not { } key ? Hidden(http) : ApiResults.From(http, await q.Access.DeletePermissionAsync(key, http.RequestAborted))).Require(AdminLevel.Project);

        g.MapGet("/groups", async (HttpContext http, Q q) => Results.Ok(await q.GroupsAsync(PageOf(http.Request), http.RequestAborted))).Require(AdminLevel.Project);
        g.MapPost("/groups", async (HttpContext http, CreateGroupRequest r, Q q) =>
        {
            var result = await q.Groups.CreateAsync(r.Name ?? "", r.DisplayName, r.Description, http.RequestAborted);
            return result.Succeeded ? Created(new { name = AccessKeys.NormalizeGroupName(r.Name) }) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);
        g.MapDelete("/groups/{id:guid}", async (HttpContext http, Guid id, Q q) =>
            await q.GroupNameAsync(id) is not { } name ? Hidden(http) : ApiResults.From(http, await q.Groups.DeleteAsync(name, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapGet("/groups/{id:guid}/members", async (HttpContext http, Guid id, Q q) =>
            await q.GroupNameAsync(id) is null ? Hidden(http) : Results.Ok(await q.MembersAsync(id, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapPut("/groups/{id:guid}/members/{userId:guid}", async (HttpContext http, Guid id, Guid userId, Q q) =>
            await q.GroupNameAsync(id) is not { } name || await q.UserAsync(userId, http) is not { } user ? Hidden(http)
                : ApiResults.From(http, await q.Groups.AddUserAsync(name, user, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapDelete("/groups/{id:guid}/members/{userId:guid}", async (HttpContext http, Guid id, Guid userId, Q q) =>
            await q.GroupNameAsync(id) is not { } name || await q.UserAsync(userId, http) is not { } user ? Hidden(http)
                : ApiResults.From(http, await q.Groups.RemoveUserAsync(name, user, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapPut("/groups/{id:guid}/roles/{roleId:guid}", async (HttpContext http, Guid id, Guid roleId, Q q) =>
            await q.GroupNameAsync(id) is not { } name || await q.RoleNameAsync(roleId) is not { } role ? Hidden(http)
                : role == Auth2Roles.GlobalAdmin && !http.Ctx().IsGlobalAdmin ? ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden)
                : ApiResults.From(http, await q.Groups.AddRoleAsync(name, role, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapDelete("/groups/{id:guid}/roles/{roleId:guid}", async (HttpContext http, Guid id, Guid roleId, Q q) =>
            await q.GroupNameAsync(id) is not { } name || await q.RoleNameAsync(roleId) is not { } role ? Hidden(http)
                : ApiResults.From(http, await q.Groups.RemoveRoleAsync(name, role, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapPut("/groups/{id:guid}/permissions/{key}", async (HttpContext http, Guid id, string key, Q q) =>
            await q.GroupNameAsync(id) is not { } name ? Hidden(http) : ApiResults.From(http, await q.Groups.AddPermissionAsync(name, key, http.RequestAborted))).Require(AdminLevel.Project);
        g.MapDelete("/groups/{id:guid}/permissions/{key}", async (HttpContext http, Guid id, string key, Q q) =>
            await q.GroupNameAsync(id) is not { } name ? Hidden(http) : ApiResults.From(http, await q.Groups.RemovePermissionAsync(name, key, http.RequestAborted))).Require(AdminLevel.Project);
    }
}

/// <summary>Services and id lookups the admin routes share. All lookups work on the application the call works on.</summary>
internal sealed class Q(Auth2DbContext db, IAccessService access, IGroupService groups, RoleManager<AppRole> roles,
    UserManager<AppUser> users, UserLookup lookup, IAuthFlowResolver flow, IOptions<Auth2ServerOptions> options)
{
    public Auth2DbContext Db => db;
    public IAccessService Access => access;
    public IGroupService Groups => groups;
    public RoleManager<AppRole> Roles => roles;

    /// <summary>A member of the application, loaded with the manager of this scope. Null when there is none.</summary>
    public async Task<AppUser?> UserAsync(Guid id, HttpContext http)
    {
        var app = await flow.GetApplicationAsync(http.RequestAborted);
        var user = await users.FindByIdAsync(id.ToString("D"));
        return user is not null && await lookup.HasRowOrSingleAsync(user, app, http.RequestAborted) ? user : null;
    }

    private bool IncludeGlobal(Auth2Application app) => app.IsGlobalProject || options.Value.Mode == ServerMode.SingleApplication;

    public async Task<string?> RoleNameAsync(Guid id)
    {
        var app = await flow.GetApplicationAsync();
        var global = IncludeGlobal(app);
        return await db.Roles.AsNoTracking().Where(r => r.Id == id && (r.ApplicationId == app.Id || (global && r.ApplicationId == null))).Select(r => r.Name).FirstOrDefaultAsync();
    }

    public async Task<string?> PermissionKeyAsync(Guid id) => await db.Permissions.AsNoTracking().Where(p => p.Id == id).Select(p => p.Key).FirstOrDefaultAsync();
    public async Task<string?> GroupNameAsync(Guid id) => await db.Groups.AsNoTracking().Where(g => g.Id == id).Select(g => g.Name).FirstOrDefaultAsync();

    public async Task<PagedResult<AdminRoleDto>> RolesAsync(PageQuery pq, CancellationToken ct)
    {
        var app = await flow.GetApplicationAsync(ct);
        var global = IncludeGlobal(app);
        var q = db.Roles.AsNoTracking().Where(r => r.ApplicationId == app.Id || (global && r.ApplicationId == null));
        if (pq.SafeSearch is { } s) q = q.Where(r => r.NormalizedName!.Contains(s.ToUpperInvariant()));
        q = pq.Descending ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(pq.Skip).Take(pq.SafePageSize).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var perms = await (from rp in db.RolePermissions.AsNoTracking() join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                           where ids.Contains(rp.RoleId) select new { rp.RoleId, p.Key }).ToListAsync(ct);
        return new PagedResult<AdminRoleDto>([.. rows.Select(r => new AdminRoleDto(r.Id, r.Name!, r.Description, r.ApplicationId is null, r.IsSystem,
            [.. perms.Where(p => p.RoleId == r.Id).Select(p => p.Key).Order(StringComparer.Ordinal)]))], pq.SafePage, pq.SafePageSize, total);
    }

    public async Task<PagedResult<AdminPermissionDto>> PermissionsAsync(PageQuery pq, CancellationToken ct)
    {
        await flow.GetApplicationAsync(ct);
        var q = db.Permissions.AsNoTracking();
        if (pq.SafeSearch is { } s) q = q.Where(p => p.Key.Contains(s.ToLowerInvariant()));
        q = pq.Descending ? q.OrderByDescending(p => p.Key) : q.OrderBy(p => p.Key);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(pq.Skip).Take(pq.SafePageSize).ToListAsync(ct);
        return new PagedResult<AdminPermissionDto>([.. rows.Select(p => new AdminPermissionDto(p.Id, p.Key, p.DisplayName, p.Description, p.Group, p.IsObsolete, p.Source.ToString()))], pq.SafePage, pq.SafePageSize, total);
    }

    public async Task<PagedResult<AdminGroupDto>> GroupsAsync(PageQuery pq, CancellationToken ct)
    {
        await flow.GetApplicationAsync(ct);
        var q = db.Groups.AsNoTracking();
        if (pq.SafeSearch is { } s) q = q.Where(g => g.Name.Contains(s.ToLowerInvariant()));
        q = pq.Descending ? q.OrderByDescending(g => g.Name) : q.OrderBy(g => g.Name);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(pq.Skip).Take(pq.SafePageSize).ToListAsync(ct);
        var ids = rows.Select(g => g.Id).ToList();
        var roleRows = await (from gr in db.GroupRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on gr.RoleId equals r.Id where ids.Contains(gr.GroupId) select new { gr.GroupId, r.Name }).ToListAsync(ct);
        var permRows = await (from gp in db.GroupPermissions.AsNoTracking() join p in db.Permissions.AsNoTracking() on gp.PermissionId equals p.Id where ids.Contains(gp.GroupId) select new { gp.GroupId, p.Key }).ToListAsync(ct);
        var counts = await db.UserGroups.AsNoTracking().Where(x => ids.Contains(x.GroupId)).GroupBy(x => x.GroupId).Select(x => new { Id = x.Key, N = x.Count() }).ToListAsync(ct);
        return new PagedResult<AdminGroupDto>([.. rows.Select(g => new AdminGroupDto(g.Id, g.Name, g.DisplayName, g.Description,
            [.. roleRows.Where(r => r.GroupId == g.Id).Select(r => r.Name!).Order(StringComparer.Ordinal)],
            [.. permRows.Where(p => p.GroupId == g.Id).Select(p => p.Key).Order(StringComparer.Ordinal)],
            counts.FirstOrDefault(c => c.Id == g.Id)?.N ?? 0))], pq.SafePage, pq.SafePageSize, total);
    }

    public async Task<IReadOnlyList<Guid>> MembersAsync(Guid groupId, CancellationToken ct) =>
        await db.UserGroups.AsNoTracking().Where(x => x.GroupId == groupId).Select(x => x.UserId).ToListAsync(ct);
}
