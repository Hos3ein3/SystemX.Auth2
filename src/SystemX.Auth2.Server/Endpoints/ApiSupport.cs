using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Endpoints;

/// <summary>Which kind of token an endpoint wants.</summary>
internal enum TokenKind
{
    /// <summary>A client-credentials token (a program).</summary>
    Machine,
    /// <summary>A token of a signed-in user.</summary>
    User,
}

/// <summary>
/// Checks the access token of an API call and picks the application <b>from the token</b> (its audience), never from the request body.
/// Answers with <see cref="ApiError"/> JSON: 401 <c>invalid_token</c>, 403 <c>forbidden</c> or <c>insufficient_scope</c>,
/// 404 <c>project_not_found</c>, 403 <c>access_denied</c> (project or application disabled).
/// </summary>
internal sealed class Auth2ApiFilter(string scope, TokenKind kind) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var text = http.RequestServices.GetRequiredService<IAuth2Localizer>();

        var auth = await http.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (!auth.Succeeded || auth.Principal is null)
        {
            http.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return ApiResults.Error(http, StatusCodes.Status401Unauthorized, Auth2ErrorCodes.InvalidToken);
        }
        var principal = auth.Principal;
        http.User = principal;

        var isMachine = principal.GetClaim(Auth2Claims.Machine) == "true";
        if ((kind == TokenKind.Machine) != isMachine) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.Forbidden);
        if (!principal.HasScope(scope)) return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.InsufficientScope);

        // The application is the audience of the token. The body of the request is never used for this.
        http.RequestServices.GetRequiredService<IClientIdAccessor>().ClientId = SystemX.Auth2.Server.Infrastructure.Auth.TokenAudience.ApplicationOf(principal);
        try
        {
            var flow = http.RequestServices.GetRequiredService<IAuthFlowResolver>();
            await flow.GetProjectAsync(http.RequestAborted);
            if (await flow.CheckGateAsync(http.RequestAborted) is not null)
                return ApiResults.Error(http, StatusCodes.Status403Forbidden, Auth2ErrorCodes.AccessDenied);
        }
        catch (ProjectNotFoundException)
        {
            return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.ProjectNotFound);
        }

        if (kind == TokenKind.User)
        {
            var user = await http.RequestServices.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(principal.GetClaim(Claims.Subject) ?? "");
            if (user is null || !await http.RequestServices.GetRequiredService<ILoginService>().IsSubjectValidAsync(user, http.RequestAborted))
                return ApiResults.Error(http, StatusCodes.Status401Unauthorized, Auth2ErrorCodes.InvalidToken);
            http.Items[UserKey] = user;
        }
        return await next(context);
    }

    internal const string UserKey = "Auth2.User";
}

internal static class ApiResults
{
    /// <summary>Builds the JSON error. The message is localized; the code is stable.</summary>
    public static IResult Error(HttpContext http, int status, string code, IEnumerable<IdentityError>? errors = null)
    {
        var text = http.RequestServices.GetRequiredService<IAuth2Localizer>();
        var list = errors?.ToList();
        var details = list is { Count: > 0 }
            ? new Dictionary<string, string[]> { ["errors"] = [.. list.Select(e => e.Description)], ["codes"] = [.. list.Select(e => e.Code)] }
            : null;
        // With Identity errors the first description is already formatted (for example "at least 10 characters").
        var message = list is { Count: > 0 } ? list[0].Description : text.Error(code);
        return Results.Json(new ApiError(code, message, details), statusCode: status);
    }

    /// <summary>Maps a stable error code to an HTTP status.</summary>
    public static int StatusFor(string code) => code switch
    {
        // invalid_token is 400 here (a bad confirmation or reset token). The auth filter answers 401 for a bad access token by itself.
        Auth2ErrorCodes.Forbidden or Auth2ErrorCodes.InsufficientScope or Auth2ErrorCodes.RegistrationDisabled
            or Auth2ErrorCodes.ProjectDisabled or Auth2ErrorCodes.ApplicationDisabled or Auth2ErrorCodes.AccessDenied or Auth2ErrorCodes.TwoFactorNotAllowed or Auth2ErrorCodes.SyncNotAllowed => StatusCodes.Status403Forbidden,
        Auth2ErrorCodes.NotFound or Auth2ErrorCodes.ProjectNotFound => StatusCodes.Status404NotFound,
        Auth2ErrorCodes.Conflict or Auth2ErrorCodes.ProjectKeyOrNameMismatch or Auth2ErrorCodes.ServerModeMismatch => StatusCodes.Status409Conflict,
        Auth2ErrorCodes.LockedOut => StatusCodes.Status423Locked,
        Auth2ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>The result of an operation as an HTTP result: 204 on success, otherwise the error.</summary>
    public static IResult From(HttpContext http, OperationResult result) =>
        result.Succeeded ? Results.NoContent() : Error(http, StatusFor(result.ErrorCode!), result.ErrorCode!, result.Errors);

    /// <summary>A sync result: 200 with the counts, otherwise the error.</summary>
    public static IResult From(HttpContext http, SyncOutcome outcome) =>
        outcome.Result.Succeeded ? Results.Ok(outcome.Response) : From(http, outcome.Result);
}

internal static class ApiEndpointExtensions
{
    /// <summary>The endpoint needs a client-credentials token with this scope.</summary>
    public static RouteHandlerBuilder RequireClient(this RouteHandlerBuilder b, string scope) => b.AddEndpointFilter(new Auth2ApiFilter(scope, TokenKind.Machine));

    /// <summary>The endpoint needs the token of a signed-in user, with this scope.</summary>
    public static RouteHandlerBuilder RequireUser(this RouteHandlerBuilder b, string scope) => b.AddEndpointFilter(new Auth2ApiFilter(scope, TokenKind.User));

    /// <summary>The user that the filter loaded.</summary>
    public static AppUser CurrentUser(this HttpContext http) => (AppUser)http.Items[Auth2ApiFilter.UserKey]!;
}
