using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Endpoints;

/// <summary>
/// Routes for client applications (client-credentials token): handshake, permission and role sync, pull of the catalogs, and
/// the access of one user. The application always comes from the token.
/// </summary>
public static class ClientEndpoints
{
    /// <summary>Maps the routes of <c>Auth2Routes</c> for handshake, sync and pull.</summary>
    public static IEndpointRouteBuilder MapAuth2Client(this IEndpointRouteBuilder routes)
    {
        routes.MapPost(Auth2Routes.Handshake, async (HttpContext http, ClientHandshakeRequest r, IClientHandshakeService handshake) =>
        {
            var (result, response) = await handshake.CheckAsync(r, http.RequestAborted);
            return result.Succeeded ? Results.Ok(response) : ApiResults.From(http, result);
        }).RequireClient(Auth2Scopes.ClientHandshake);

        routes.MapPost(Auth2Routes.PermissionsSync, async (HttpContext http, PermissionSyncRequest r, IPermissionSyncService sync) =>
            ApiResults.From(http, await sync.SyncAsync(r, http.RequestAborted))).RequireClient(Auth2Scopes.PermissionsSync);

        routes.MapPost(Auth2Routes.RolesSync, async (HttpContext http, RoleSyncRequest r, IRoleSyncService sync) =>
            ApiResults.From(http, await sync.SyncAsync(r, http.RequestAborted))).RequireClient(Auth2Scopes.RolesSync);

        routes.MapPost(Auth2Routes.GroupsSync, async (HttpContext http, GroupSyncRequest r, IGroupSyncService sync) =>
            ApiResults.From(http, await sync.SyncAsync(r, http.RequestAborted))).RequireClient(Auth2Scopes.GroupsSync);

        routes.MapPost(Auth2Routes.ScopesSync, async (HttpContext http, ScopeSyncRequest r, IScopeSyncService sync) =>
            ApiResults.From(http, await sync.SyncAsync(r, http.RequestAborted))).RequireClient(Auth2Scopes.ScopesSync);

        routes.MapGet(Auth2Routes.ExternalProviders, async (HttpContext http, SystemX.Auth2.Server.Infrastructure.External.ExternalFlow flow) =>
            Results.Ok(await flow.ListAsync(http.RequestAborted))).RequireClient(Auth2Scopes.ClientHandshake);

        routes.MapGet(Auth2Routes.Scopes, async (HttpContext http, IScopeSyncService sync) =>
            Results.Ok(await sync.ListAsync(http.RequestAborted))).RequireClient(Auth2Scopes.ScopesSync);

        routes.MapGet(Auth2Routes.Permissions, async (HttpContext http, IAccessService access) =>
            Results.Ok(await access.ListPermissionsAsync(http.RequestAborted))).RequireClient(Auth2Scopes.PermissionsSync);

        routes.MapGet(Auth2Routes.Roles, async (HttpContext http, IAccessService access) =>
            Results.Ok(await access.ListRolesAsync(http.RequestAborted))).RequireClient(Auth2Scopes.RolesSync);

        routes.MapGet(Auth2Routes.Groups, async (HttpContext http, IGroupService groups) =>
            Results.Ok(await groups.ListAsync(http.RequestAborted))).RequireClient(Auth2Scopes.GroupsRead);

        // A program (with the admin scope) reads one user's access in the token's application.
        routes.MapGet(Auth2Routes.UserAccess, async (HttpContext http, Guid id, UserManager<AppUser> users, UserLookup lookup, IAuthFlowResolver flow, IAccessService access) =>
        {
            var user = await users.FindByIdAsync(id.ToString("D"));
            var app = await flow.GetProjectAsync(http.RequestAborted);
            if (user is null || !await lookup.IsMemberAsync(user, app, http.RequestAborted))
                return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.NotFound);
            return Results.Ok(await access.GetCurrentUserAsync(user, [], http.RequestAborted));
        }).RequireClient(Auth2Scopes.Admin);

        return routes;
    }
}
