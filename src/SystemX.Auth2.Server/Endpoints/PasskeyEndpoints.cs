using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Endpoints;

/// <summary>Body of <c>/account/passkey/register</c>.</summary>
/// <param name="Credential">The credential from <c>navigator.credentials.create</c> as JSON (the browser's <c>toJSON()</c>).</param>
/// <param name="Name">A name for the passkey ("MacBook").</param>
public sealed record PasskeyRegisterRequest(JsonElement Credential, string? Name);

/// <summary>Body of <c>/account/passkey/request-options</c>.</summary>
/// <param name="Login">User name, email or phone, or null for a usernameless sign-in.</param>
public sealed record PasskeyRequestOptionsRequest(string? Login);

/// <summary>Body of <c>/account/passkey/signin</c>.</summary>
/// <param name="Credential">The credential from <c>navigator.credentials.get</c> as JSON.</param>
/// <param name="ReturnUrl">Where to go afterwards (checked like every return URL).</param>
public sealed record PasskeySignInRequest(JsonElement Credential, string? ReturnUrl);

/// <summary>Body of the rename route.</summary>
/// <param name="Name">The new name.</param>
public sealed record PasskeyRenameRequest(string Name);

/// <summary>
/// The JSON routes of passkeys. Registration and management need the hosted-UI session (cookie) and an anti-forgery header
/// (<c>RequestVerificationToken</c>). Sign-in is anonymous and rate limited. The application comes from the <c>client_id</c> query value
/// (or from the session cookie). Details for the UI: <c>docs/ui-handover.md</c>.
/// </summary>
public static class PasskeyEndpoints
{
    /// <summary>Maps the routes.</summary>
    public static IEndpointRouteBuilder MapAuth2Passkeys(this IEndpointRouteBuilder routes)
    {

        // ---- registration and management (signed in)
        routes.MapPost(Auth2Routes.PasskeyCreationOptions, (HttpContext http, string? client_id) => Session(http, client_id, async (user, svc) =>
        {
            var r = await svc.CreationOptionsAsync(user, http.RequestAborted);
            return r.Succeeded ? Results.Content(r.Value!, "application/json") : ApiResults.Error(http, StatusCodes.Status400BadRequest, r.Result.ErrorCode!);
        }));
        routes.MapPost(Auth2Routes.PasskeyRegister, (HttpContext http, string? client_id, PasskeyRegisterRequest body) => Session(http, client_id, async (user, svc) =>
        {
            var r = await svc.RegisterAsync(user, body.Credential.GetRawText(), body.Name, http.RequestAborted);
            return r.Succeeded ? Results.Ok(r.Value) : ApiResults.Error(http, StatusCodes.Status400BadRequest, r.Result.ErrorCode!);
        }));
        routes.MapGet(Auth2Routes.Passkeys, (HttpContext http, string? client_id) => Session(http, client_id, async (user, svc) => Results.Ok(await svc.ListAsync(user, http.RequestAborted)), checkToken: false));
        routes.MapMethods(Auth2Routes.PasskeyById, ["PATCH"], (HttpContext http, string id, string? client_id, PasskeyRenameRequest body) => Session(http, client_id, async (user, svc) =>
        {
            var r = await svc.RenameAsync(user, id, body.Name, http.RequestAborted);
            return r.Succeeded ? Results.NoContent() : ApiResults.Error(http, r.ErrorCode == Auth2ErrorCodes.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, r.ErrorCode!);
        }));
        routes.MapDelete(Auth2Routes.PasskeyById, (HttpContext http, string id, string? client_id) => Session(http, client_id, async (user, svc) =>
        {
            var r = await svc.RemoveAsync(user, id, http.RequestAborted);
            return r.Succeeded ? Results.NoContent() : ApiResults.Error(http, r.ErrorCode == Auth2ErrorCodes.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, r.ErrorCode!);
        }));

        // ---- sign-in (anonymous)
        routes.MapPost(Auth2Routes.PasskeyRequestOptions, async (HttpContext http, string? client_id, PasskeyRequestOptionsRequest? body, IClientIdAccessor accessor, IPasskeyService svc) =>
        {
            accessor.ClientId ??= client_id;
            try
            {
                var r = await svc.RequestOptionsAsync(body?.Login, http.RequestAborted);
                return r.Succeeded ? Results.Content(r.Value!, "application/json") : ApiResults.Error(http, StatusCodes.Status400BadRequest, r.Result.ErrorCode!);
            }
            catch (ProjectNotFoundException) { return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.ProjectNotFound); }
        });
        routes.MapPost(Auth2Routes.PasskeySignIn, async (HttpContext http, string? client_id, PasskeySignInRequest body, IClientIdAccessor accessor, IPasskeyService svc,
            IAuthFlowResolver flow, IReturnUrlValidator urls) =>
        {
            accessor.ClientId ??= client_id;
            try
            {
                var result = await svc.SignInAsync(body.Credential.GetRawText(), http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), http.RequestAborted);
                if (!result.Succeeded)
                    return result.ErrorCode == Auth2ErrorCodes.TwoFactorRequired
                        ? Results.Ok(new { succeeded = false, errorCode = result.ErrorCode, twoFactorToken = result.TwoFactorToken })
                        : ApiResults.Error(http, result.ErrorCode == Auth2ErrorCodes.RateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest, result.ErrorCode ?? Auth2ErrorCodes.InvalidCredentials);
                var settings = await flow.GetEffectiveAsync(http.RequestAborted);
                await Auth2Cookie.SignInAsync(http, result.User!, result.Application!, settings.Application, settings, result.Amr, result.SessionId, result.IdentityProvider);
                return Results.Ok(new { succeeded = true, redirectTo = urls.Resolve(body.ReturnUrl, settings.Project.Redirects.RedirectAfterLogin, settings) });
            }
            catch (ProjectNotFoundException) { return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.ProjectNotFound); }
        });
        return routes;
    }

    /// <summary>Runs an action for the signed-in user of the session cookie.</summary>
    private static async Task<IResult> Session(HttpContext http, string? clientId, Func<AppUser, IPasskeyService, Task<IResult>> action, bool checkToken = true)
    {
        var session = await http.AuthenticateAsync(Auth2Cookie.Scheme);
        if (!session.Succeeded) return ApiResults.Error(http, StatusCodes.Status401Unauthorized, Auth2ErrorCodes.InvalidToken);
        if (checkToken)
        {
            try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException) { return ApiResults.Error(http, StatusCodes.Status400BadRequest, Auth2ErrorCodes.ValidationFailed); }
        }
        http.RequestServices.GetRequiredService<IClientIdAccessor>().ClientId = session.Principal!.FindFirst(Auth2Claims.ClientId)?.Value ?? clientId;
        var user = await http.RequestServices.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(session.Principal.FindFirst("sub")?.Value ?? "");
        var app = await http.RequestServices.GetRequiredService<IAuthFlowResolver>().GetProjectAsync(http.RequestAborted);
        if (user is null || session.Principal.FindFirst(Auth2Claims.ProjectId)?.Value != app.Id.ToString("D")
            || !await http.RequestServices.GetRequiredService<ILoginService>().IsSubjectValidAsync(user, http.RequestAborted))
            return ApiResults.Error(http, StatusCodes.Status401Unauthorized, Auth2ErrorCodes.InvalidToken);
        return await action(user, http.RequestServices.GetRequiredService<IPasskeyService>());
    }
}
