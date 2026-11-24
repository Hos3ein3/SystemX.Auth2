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
/// The caller is found in their <b>home</b> project (the application in the audience of the token belongs to it). A global admin may work
/// on another project through the route <c>/projects/{projectId}/...</c>; a project admin never can.
/// </summary>
internal sealed class AdminApiFilter(AdminLevel level) : IEndpointFilter
{
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
        var single = options.Mode == ServerMode.SingleProject;

        // Who is calling? Found in a separate scope, so the request scope is still free to work on the target application.
        Project home; AppUser admin; bool isGlobal, isProjectAdmin;
        await using (var child = http.RequestServices.CreateAsyncScope())
        {
            var sp = child.ServiceProvider;
            sp.GetRequiredService<IClientIdAccessor>().ClientId = principal.GetAudiences().FirstOrDefault();
            try { home = await sp.GetRequiredService<IAuthFlowResolver>().GetProjectAsync(http.RequestAborted); }
            catch (ProjectNotFoundException) { return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.ProjectNotFound); }
            if (await sp.GetRequiredService<IAuthFlowResolver>().CheckGateAsync(http.RequestAborted) is not null) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.AccessDenied);

            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var found = await users.FindByIdAsync(principal.GetClaim(Claims.Subject) ?? "");
            if (found is null || !await sp.GetRequiredService<ILoginService>().IsSubjectValidAsync(found, http.RequestAborted))
                return ApiResults.Error(http, StatusCodes.Status401Unauthorized, Auth2ErrorCodes.InvalidToken);
            admin = found;
            // GlobalAdmin is a global role: it counts only in the _global project (or the single project).
            isGlobal = (home.IsGlobalProject || single) && await users.IsInRoleAsync(admin, Auth2Roles.GlobalAdmin);
            isProjectAdmin = await users.IsInRoleAsync(admin, Auth2Roles.ProjectAdmin);
        }
        if (level == AdminLevel.Global ? !isGlobal : !(isGlobal || isProjectAdmin))
            return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);

        // Which project does the call work on? The route says (/projects/{projectId}/...). A global admin may use any project,
        // a project admin only the home project. Another project looks like "not found" to a project admin.
        var target = home;
        if (!single && http.Request.RouteValues["projectId"] is string routeId && Guid.TryParse(routeId, out var wanted) && wanted != home.Id)
        {
            if (!isGlobal) return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.NotFound);
            var found = await http.RequestServices.GetRequiredService<IProjectService>().FindAsync(wanted, http.RequestAborted);
            if (found is null) return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.NotFound);
            target = found;
        }
        else if (single && http.Request.RouteValues["projectId"] is string single_id && Guid.TryParse(single_id, out var w2) && w2 != home.Id)
            return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.NotFound);

        // The request scope now works on the target project.
        http.RequestServices.GetRequiredService<IProjectSelection>().ProjectKey = single ? null : target.ProjectKey;
        await http.RequestServices.GetRequiredService<IProjectContext>().GetRequiredAsync(http.RequestAborted);

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
        var p = g.MapGroup("/projects/{projectId:guid}");   // project-level data: the project comes from the route
        MapProjects(g);
        MapApplications(g);
        MapUsers(p);
        MapAccess(p);
        MapExternal(g, p);

        // Keys (global admin only). Rotation: new keys sign from now on, old keys keep checking old tokens until they are removed.
        g.MapGet("/keys", async (HttpContext http, IKeyRing keys) => Results.Ok(await keys.ListAsync(http.RequestAborted))).Require(AdminLevel.Global);
        g.MapPost("/keys/rotate", async (HttpContext http, IKeyRing keys, IAuditService audit) =>
        {
            var list = await keys.RotateAsync(http.RequestAborted);
            await audit.WriteAsync(new AuditEntry("keys.rotated", null, TargetType: "keys"), http.RequestAborted);
            return Results.Ok(list);
        }).Require(AdminLevel.Global);

        p.MapGet("/audit-events", async (HttpContext http, IAuditReader reader, string? action, Guid? actorUserId, string? targetType, DateTimeOffset? from, DateTimeOffset? to, bool? all) =>
        {
            if (all == true && !http.Ctx().IsGlobalAdmin) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
            return Results.Ok(await reader.QueryAsync(PageOf(http.Request), action, actorUserId, targetType, from, to, all == true, http.RequestAborted));
        }).Require(AdminLevel.Project);

        return routes;
    }

    // ------------------------------------------------------------------ external identity

    private static async Task<string> BodyAsync(HttpContext http)
    {
        using var reader = new StreamReader(http.Request.Body);
        return await reader.ReadToEndAsync(http.RequestAborted);
    }

    private static IResult NotFoundOrFail(HttpContext http, OperationResult result) =>
        ApiResults.From(http, result);

    private static void MapExternal(RouteGroupBuilder g, RouteGroupBuilder p)
    {
        p.MapGet("/external-provider-types", (HttpContext http, IAdminExternalService svc) => Results.Ok(svc.Types())).Require(AdminLevel.Project);

        p.MapGet("/external-providers", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            return await svc.ListProvidersAsync(id, http.RequestAborted) is { } list ? Results.Ok(list) : Hidden(http);
        }).Require(AdminLevel.Project);

        p.MapPost("/external-providers", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, provider) = await svc.CreateProviderAsync(id, await BodyAsync(http), http.Ctx().IsGlobalAdmin, http.RequestAborted);
            return result.Succeeded ? Created(provider!) : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);

        p.MapPut("/external-providers/{name}", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, string name, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, provider) = await svc.UpdateProviderAsync(id, name, await BodyAsync(http), http.Ctx().IsGlobalAdmin, ParseVersion(http.Request.Headers.IfMatch.FirstOrDefault()), http.RequestAborted);
            return result.Succeeded ? Results.Ok(provider) : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);

        p.MapPost("/external-providers/{name}/rotate-secret", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, string name, RotateProviderSecretRequest r, IAdminExternalService svc) =>
            !Allowed(http, id) ? Hidden(http) : NotFoundOrFail(http, await svc.RotateSecretAsync(id, name, r, http.RequestAborted))).Require(AdminLevel.Project);

        p.MapDelete("/external-providers/{name}", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, string name, IAdminExternalService svc) =>
            !Allowed(http, id) ? Hidden(http) : NotFoundOrFail(http, await svc.DeleteProviderAsync(id, name, http.RequestAborted))).Require(AdminLevel.Project);

        p.MapPost("/external-providers/{name}/test", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, string name, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, test) = await svc.TestProviderAsync(id, name, http.RequestAborted);
            return result.Succeeded ? Results.Ok(test) : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);

        p.MapPost("/external-providers/{name}/ldap/test-user", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, string name, LdapTestUserRequest r, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, found) = await svc.LdapTestUserAsync(id, name, r.UserName, http.RequestAborted);
            return result.Succeeded ? Results.Ok(found) : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);

        p.MapPost("/external-providers/{name}/saml/refresh-metadata", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, string name, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, test) = await svc.SamlRefreshMetadataAsync(id, name, http.RequestAborted);
            return result.Succeeded ? Results.Ok(test) : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);

        p.MapPost("/external-providers/{name}/saml/generate-sp-certificate", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, string name, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, cert) = await svc.SamlGenerateCertificateAsync(id, name, http.RequestAborted);
            return result.Succeeded ? Results.Ok(cert) : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);

        // ---- mappings
        p.MapGet("/external-group-mappings", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, IAdminExternalService svc) =>
            !Allowed(http, id) ? Hidden(http) : Results.Ok(await svc.ListMappingsAsync(id, http.RequestAborted))).Require(AdminLevel.Project);
        p.MapPost("/external-group-mappings", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, ExternalMappingRequest r, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, mapping) = await svc.CreateMappingAsync(id, r, http.RequestAborted);
            return result.Succeeded ? Created(mapping!) : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);
        p.MapPut("/external-group-mappings/{mappingId:guid}", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, Guid mappingId, ExternalMappingRequest r, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var (result, mapping) = await svc.UpdateMappingAsync(id, mappingId, r, http.RequestAborted);
            return result.Succeeded ? Results.Ok(mapping) : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);
        p.MapDelete("/external-group-mappings/{mappingId:guid}", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, Guid mappingId, IAdminExternalService svc) =>
            !Allowed(http, id) ? Hidden(http) : NotFoundOrFail(http, await svc.DeleteMappingAsync(id, mappingId, http.RequestAborted))).Require(AdminLevel.Project);

        // ---- home-realm discovery
        p.MapGet("/email-domain-routes", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            return await svc.ListEmailDomainRoutesAsync(id, http.RequestAborted) is { } list ? Results.Ok(list) : Hidden(http);
        }).Require(AdminLevel.Project);
        p.MapPut("/email-domain-routes", async (HttpContext http, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "projectId")] Guid id, List<EmailDomainRouteDto> routes, IAdminExternalService svc) =>
        {
            if (!Allowed(http, id)) return Hidden(http);
            var result = await svc.SetEmailDomainRoutesAsync(id, routes, http.RequestAborted);
            return result.Succeeded ? Results.NoContent() : NotFoundOrFail(http, result);
        }).Require(AdminLevel.Project);

        // ---- users
        p.MapGet("/users/{id:guid}/external-logins", async (HttpContext http, Guid id, IAdminExternalService svc) =>
        {
            var (result, list) = await svc.ListUserLoginsAsync(id, http.RequestAborted);
            return result.Succeeded ? Results.Ok(list) : Hidden(http);
        }).Require(AdminLevel.Project);
        p.MapDelete("/users/{id:guid}/external-logins/{provider}", async (HttpContext http, Guid id, string provider, bool? force, IAdminExternalService svc) =>
        {
            if (force == true && !http.Ctx().IsGlobalAdmin) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
            var result = await svc.RemoveUserLoginAsync(id, provider, force == true, http.RequestAborted);
            return result.Succeeded ? Results.NoContent() : result.ErrorCode == Auth2ErrorCodes.NotFound ? Hidden(http) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);
        p.MapGet("/users/{id:guid}/passkeys", async (HttpContext http, Guid id, IAdminExternalService svc) =>
        {
            var (result, list) = await svc.ListUserPasskeysAsync(id, http.RequestAborted);
            return result.Succeeded ? Results.Ok(list) : Hidden(http);
        }).Require(AdminLevel.Project);
        p.MapDelete("/users/{id:guid}/passkeys/{passkeyId}", async (HttpContext http, Guid id, string passkeyId, IAdminExternalService svc) =>
        {
            var result = await svc.RemoveUserPasskeyAsync(id, passkeyId, http.RequestAborted);
            return result.Succeeded ? Results.NoContent() : result.ErrorCode == Auth2ErrorCodes.NotFound ? Hidden(http) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);

        // ---- authenticator models that cannot be registered (global admin only)
        g.MapGet("/passkeys/blocked-aaguids", async (HttpContext http, IAdminExternalService svc) => Results.Ok(await svc.GetBlockedAaguidsAsync(http.RequestAborted))).Require(AdminLevel.Global);
        g.MapPut("/passkeys/blocked-aaguids", async (HttpContext http, BlockedAaguidsDto body, IAdminExternalService svc) =>
        {
            var result = await svc.SetBlockedAaguidsAsync(body.Aaguids, http.RequestAborted);
            return result.Succeeded ? Results.NoContent() : ApiResults.From(http, result);
        }).Require(AdminLevel.Global);
    }

    // ------------------------------------------------------------------ projects

    private static void MapProjects(RouteGroupBuilder g)
    {
        g.MapGet("/projects", async (HttpContext http, IAdminProjectService projects) =>
        {
            if (http.Ctx().IsGlobalAdmin) return Results.Ok(await projects.ListAsync(PageOf(http.Request), http.RequestAborted));
            var own = await projects.GetAsync(http.Ctx().Home.Id, http.RequestAborted);   // a project admin sees only the own project
            return Results.Ok(new PagedResult<ProjectDto>(own is null ? [] : [own], 1, 50, own is null ? 0 : 1));
        }).Require(AdminLevel.Project);

        g.MapGet("/projects/{projectId:guid}", async (HttpContext http, Guid projectId, IAdminProjectService projects) =>
            await projects.GetAsync(projectId, http.RequestAborted) is { } project ? Results.Ok(project) : Hidden(http)).Require(AdminLevel.Project);

        g.MapPost("/projects", async (HttpContext http, CreateProjectRequest r, IAdminProjectService projects) =>
        {
            var (result, response) = await projects.CreateAsync(r, http.RequestAborted);
            return result.Succeeded ? Created(response!) : ApiResults.From(http, result);
        }).Require(AdminLevel.Global);

        g.MapPut("/projects/{projectId:guid}", async (HttpContext http, Guid projectId, UpdateProjectRequest r, IAdminProjectService projects) =>
        {
            // A project admin may rename the own project, but only a global admin may disable or enable it.
            if (!http.Ctx().IsGlobalAdmin)
            {
                var current = await projects.GetAsync(projectId, http.RequestAborted);
                if (current is not null && current.IsEnabled != r.IsEnabled) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
            }
            var (result, project) = await projects.UpdateAsync(projectId, r, http.Ctx().UserId, http.RequestAborted);
            return result.Succeeded ? Results.Ok(project) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);

        g.MapDelete("/projects/{projectId:guid}", async (HttpContext http, Guid projectId, IAdminProjectService projects) =>
            ApiResults.From(http, await projects.DeleteAsync(projectId, http.RequestAborted))).Require(AdminLevel.Global);

        g.MapGet("/projects/{projectId:guid}/settings", async (HttpContext http, Guid projectId, IAdminProjectService projects) =>
        {
            if (await projects.GetSettingsAsync(projectId, http.RequestAborted) is not { } s) return Hidden(http);
            http.Response.Headers.ETag = $"\"v{s.Version}\"";
            return Results.Content(SettingsJson.SerializeRedacted(s.Settings, http.RequestServices.GetRequiredService<SystemX.Auth2.Server.Infrastructure.External.IExternalSecretProtector>().Redact), "application/json");
        }).Require(AdminLevel.Project);

        g.MapPut("/projects/{projectId:guid}/settings", async (HttpContext http, Guid projectId, IAdminProjectService projects) =>
        {
            Domain.Settings.ProjectSettings settings;
            try
            {
                using var reader = new StreamReader(http.Request.Body);
                settings = SettingsJson.Deserialize(await reader.ReadToEndAsync(http.RequestAborted));
            }
            catch (JsonException) { return ApiResults.Error(http, StatusCodes.Status400BadRequest, Auth2ErrorCodes.ValidationFailed); }
            var (result, version) = await projects.UpdateSettingsAsync(projectId, settings, http.Ctx().IsGlobalAdmin, ParseVersion(http.Request.Headers.IfMatch.FirstOrDefault()), http.RequestAborted);
            if (!result.Succeeded) return ApiResults.From(http, result);
            http.Response.Headers.ETag = $"\"v{version}\"";
            return Results.NoContent();
        }).Require(AdminLevel.Project);

        // How the project sends email and SMS. Holds a password, so only a global admin may see or change it.
        g.MapGet("/projects/{projectId:guid}/messaging", async (HttpContext http, Guid projectId, IAdminMessagingService svc) =>
            await svc.GetProjectAsync(projectId, http.RequestAborted) is { } m ? Results.Ok(m) : Hidden(http)).Require(AdminLevel.Global);

        g.MapPut("/projects/{projectId:guid}/messaging", async (HttpContext http, Guid projectId, MessagingUpdate update, IAdminMessagingService svc) =>
            ApiResults.From(http, await svc.UpdateProjectAsync(projectId, update, http.Ctx().UserId, http.RequestAborted))).Require(AdminLevel.Global);
    }

    // ------------------------------------------------------------------ applications

    private static string ServerUrl(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<Auth2ServerOptions>>().Value.Issuer?.TrimEnd('/')
        ?? $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";

    private static void MapApplications(RouteGroupBuilder g)
    {
        var a = g.MapGroup("/projects/{projectId:guid}/applications");

        a.MapGet("/", async (HttpContext http, Guid projectId, IAdminApplicationService apps) =>
            Results.Ok(await apps.ListAsync(projectId, http.RequestAborted))).Require(AdminLevel.Project);

        a.MapGet("/{applicationId:guid}", async (HttpContext http, Guid projectId, Guid applicationId, IAdminApplicationService apps) =>
            await apps.GetAsync(projectId, applicationId, http.RequestAborted) is { } app ? Results.Ok(app) : Hidden(http)).Require(AdminLevel.Project);

        a.MapPost("/", async (HttpContext http, Guid projectId, CreateApplicationRequest r, IAdminApplicationService apps) =>
        {
            var (result, response) = await apps.CreateAsync(projectId, r, ServerUrl(http), http.RequestAborted);
            return result.Succeeded ? Created(response!) : ApiResults.From(http, result);
        }).Require(AdminLevel.Global);

        a.MapPut("/{applicationId:guid}", async (HttpContext http, Guid projectId, Guid applicationId, UpdateApplicationRequest r, IAdminApplicationService apps) =>
        {
            var (result, app) = await apps.UpdateAsync(projectId, applicationId, r, http.Ctx().UserId, http.RequestAborted);
            return result.Succeeded ? Results.Ok(app) : ApiResults.From(http, result);
        }).Require(AdminLevel.Global);

        a.MapDelete("/{applicationId:guid}", async (HttpContext http, Guid projectId, Guid applicationId, IAdminApplicationService apps) =>
            ApiResults.From(http, await apps.DeleteAsync(projectId, applicationId, http.RequestAborted))).Require(AdminLevel.Global);

        // The copy-paste client configuration (the secret is never part of it, it is shown once at creation or rotation).
        a.MapGet("/{applicationId:guid}/setup", async (HttpContext http, Guid projectId, Guid applicationId, IAdminApplicationService apps) =>
        {
            var (result, setup) = await apps.GetSetupAsync(projectId, applicationId, ServerUrl(http), http.RequestAborted);
            return result.Succeeded ? Results.Ok(setup) : Hidden(http);
        }).Require(AdminLevel.Project);

        a.MapPost("/{applicationId:guid}/secret/rotate", async (HttpContext http, Guid projectId, Guid applicationId, IAdminApplicationService apps) =>
        {
            var (result, setup) = await apps.RotateSecretAsync(projectId, applicationId, http.Ctx().IsGlobalAdmin, ServerUrl(http), http.RequestAborted);
            return result.Succeeded ? Results.Ok(new RotateSecretResponse(setup!.ClientSecret!, setup)) : ApiResults.From(http, result);
        }).Require(AdminLevel.Project);

        a.MapGet("/{applicationId:guid}/settings", async (HttpContext http, Guid projectId, Guid applicationId, IAdminApplicationService apps) =>
        {
            var (result, settings, version) = await apps.GetSettingsAsync(projectId, applicationId, http.RequestAborted);
            if (!result.Succeeded) return Hidden(http);
            http.Response.Headers.ETag = $"\"v{version}\"";
            return Results.Ok(settings);
        }).Require(AdminLevel.Project);

        a.MapPut("/{applicationId:guid}/settings", async (HttpContext http, Guid projectId, Guid applicationId, Domain.Settings.ClientApplicationSettings settings, IAdminApplicationService apps) =>
        {
            var (result, version) = await apps.UpdateSettingsAsync(projectId, applicationId, settings, http.Ctx().IsGlobalAdmin, ParseVersion(http.Request.Headers.IfMatch.FirstOrDefault()), http.RequestAborted);
            if (!result.Succeeded) return ApiResults.From(http, result);
            http.Response.Headers.ETag = $"\"v{version}\"";
            return Results.NoContent();
        }).Require(AdminLevel.Project);

        // An application may send through its own provider (only a global admin: it holds a password).
        a.MapGet("/{applicationId:guid}/messaging", async (HttpContext http, Guid projectId, Guid applicationId, IAdminMessagingService svc) =>
            await svc.GetApplicationAsync(projectId, applicationId, http.RequestAborted) is { } m ? Results.Ok(m) : Hidden(http)).Require(AdminLevel.Global);

        a.MapPut("/{applicationId:guid}/messaging", async (HttpContext http, Guid projectId, Guid applicationId, MessagingUpdate update, IAdminMessagingService svc) =>
            ApiResults.From(http, await svc.UpdateApplicationAsync(projectId, applicationId, update, http.Ctx().UserId, http.RequestAborted))).Require(AdminLevel.Global);

        a.MapDelete("/{applicationId:guid}/messaging", async (HttpContext http, Guid projectId, Guid applicationId, IAdminMessagingService svc) =>
            ApiResults.From(http, await svc.ClearApplicationAsync(projectId, applicationId, http.Ctx().UserId, http.RequestAborted))).Require(AdminLevel.Global);

        a.MapGet("/{applicationId:guid}/redirect-uris", async (HttpContext http, Guid projectId, Guid applicationId, IAdminApplicationService apps) =>
            await apps.GetAsync(projectId, applicationId, http.RequestAborted) is { } app ? Results.Ok(new RedirectUrisDto(app.RedirectUris, app.PostLogoutRedirectUris)) : Hidden(http))
            .Require(AdminLevel.Project);

        a.MapPut("/{applicationId:guid}/redirect-uris", async (HttpContext http, Guid projectId, Guid applicationId, RedirectUrisDto r, IAdminApplicationService apps) =>
        {
            var current = await apps.GetAsync(projectId, applicationId, http.RequestAborted);
            if (current is null) return Hidden(http);
            var (result, _) = await apps.UpdateAsync(projectId, applicationId,
                new UpdateApplicationRequest(current.Name, current.IsEnabled, r.RedirectUris, r.PostLogoutRedirectUris), http.Ctx().UserId, http.RequestAborted);
            return ApiResults.From(http, result);
        }).Require(AdminLevel.Global);
    }

    /// <summary>The route project is checked by the filter already. This only repeats the rule: a global admin may use any project id, a project admin only the own one.</summary>
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
            if (await q.Db.Roles.AnyAsync(r => r.ProjectId == app.Id && r.NormalizedName == Auth2Roles.ProjectAdmin.ToUpperInvariant(), http.RequestAborted) == false)
            {
                var created = await q.Roles.CreateAsync(new AppRole(Auth2Roles.ProjectAdmin) { ProjectId = app.Id, IsSystem = true, Description = "Manages this application." });
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
        var app = await flow.GetProjectAsync(http.RequestAborted);
        var user = await users.FindByIdAsync(id.ToString("D"));
        return user is not null && await lookup.HasRowOrSingleAsync(user, app, http.RequestAborted) ? user : null;
    }

    private bool IncludeGlobal(Project app) => app.IsGlobalProject || options.Value.Mode == ServerMode.SingleProject;

    public async Task<string?> RoleNameAsync(Guid id)
    {
        var app = await flow.GetProjectAsync();
        var global = IncludeGlobal(app);
        return await db.Roles.AsNoTracking().Where(r => r.Id == id && (r.ProjectId == app.Id || (global && r.ProjectId == null))).Select(r => r.Name).FirstOrDefaultAsync();
    }

    public async Task<string?> PermissionKeyAsync(Guid id) => await db.Permissions.AsNoTracking().Where(p => p.Id == id).Select(p => p.Key).FirstOrDefaultAsync();
    public async Task<string?> GroupNameAsync(Guid id) => await db.Groups.AsNoTracking().Where(g => g.Id == id).Select(g => g.Name).FirstOrDefaultAsync();

    public async Task<PagedResult<AdminRoleDto>> RolesAsync(PageQuery pq, CancellationToken ct)
    {
        var app = await flow.GetProjectAsync(ct);
        var global = IncludeGlobal(app);
        var q = db.Roles.AsNoTracking().Where(r => r.ProjectId == app.Id || (global && r.ProjectId == null));
        if (pq.SafeSearch is { } s) q = q.Where(r => r.NormalizedName!.Contains(s.ToUpperInvariant()));
        q = pq.Descending ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(pq.Skip).Take(pq.SafePageSize).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var perms = await (from rp in db.RolePermissions.AsNoTracking() join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                           where ids.Contains(rp.RoleId) select new { rp.RoleId, p.Key }).ToListAsync(ct);
        return new PagedResult<AdminRoleDto>([.. rows.Select(r => new AdminRoleDto(r.Id, r.Name!, r.Description, r.ProjectId is null, r.IsSystem,
            [.. perms.Where(p => p.RoleId == r.Id).Select(p => p.Key).Order(StringComparer.Ordinal)]))], pq.SafePage, pq.SafePageSize, total);
    }

    public async Task<PagedResult<AdminPermissionDto>> PermissionsAsync(PageQuery pq, CancellationToken ct)
    {
        await flow.GetProjectAsync(ct);
        var q = db.Permissions.AsNoTracking();
        if (pq.SafeSearch is { } s) q = q.Where(p => p.Key.Contains(s.ToLowerInvariant()));
        q = pq.Descending ? q.OrderByDescending(p => p.Key) : q.OrderBy(p => p.Key);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(pq.Skip).Take(pq.SafePageSize).ToListAsync(ct);
        return new PagedResult<AdminPermissionDto>([.. rows.Select(p => new AdminPermissionDto(p.Id, p.Key, p.DisplayName, p.Description, p.Group, p.IsObsolete, p.Source.ToString()))], pq.SafePage, pq.SafePageSize, total);
    }

    public async Task<PagedResult<AdminGroupDto>> GroupsAsync(PageQuery pq, CancellationToken ct)
    {
        await flow.GetProjectAsync(ct);
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
