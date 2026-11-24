using Microsoft.AspNetCore.Antiforgery;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Endpoints;

/// <summary>Public routes of the external sign-in: the list of providers and the start of a sign-in.</summary>
public static class ExternalEndpoints
{
    /// <summary>Route of the provider list.</summary>
    public const string Providers = "/connect/external/providers";
    /// <summary>Route that starts a sign-in.</summary>
    public const string Challenge = "/connect/external/challenge";

    /// <summary>Maps the routes.</summary>
    public static IEndpointRouteBuilder MapAuth2External(this IEndpointRouteBuilder routes)
    {
        // The list has no secrets: names, button texts and kinds. The application comes from client_id (optional in single mode).
        routes.MapGet(Providers, async (HttpContext http, string? client_id, string? hint, IClientIdAccessor accessor, ExternalFlow flow) =>
        {
            accessor.ClientId ??= client_id;
            try
            {
                var list = await flow.ListAsync(http.RequestAborted);
                return Results.Ok(new ExternalProvidersResponse(list, await flow.DiscoverAsync(hint, http.RequestAborted)));
            }
            catch (ProjectNotFoundException)
            {
                return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.ProjectNotFound);
            }
        });

        // A form post with an anti-forgery token (like the page). Answers with a redirect to the provider.
        routes.MapPost(Challenge, async (HttpContext http, IAntiforgery antiforgery, IClientIdAccessor accessor, ExternalFlow flow) =>
        {
            try { await antiforgery.ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException) { return ApiResults.Error(http, StatusCodes.Status400BadRequest, Auth2ErrorCodes.ValidationFailed); }
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            accessor.ClientId ??= form["client_id"].ToString() is { Length: > 0 } c ? c : null;
            try
            {
                var start = await flow.StartAsync(http, form["provider"].ToString(), form["returnUrl"], form["hint"], null, http.RequestAborted);
                if (!start.Succeeded) return ApiResults.Error(http, StatusCodes.Status400BadRequest, start.ErrorCode!);
                // SAML with the POST binding: the UI must post this form to the provider.
                return start.PostFields is not null ? Results.Ok(new { postAction = start.RedirectTo, postFields = start.PostFields }) : Results.Redirect(start.RedirectTo!);
            }
            catch (ProjectNotFoundException)
            {
                return ApiResults.Error(http, StatusCodes.Status404NotFound, Auth2ErrorCodes.ProjectNotFound);
            }
        });
        return routes;
    }
}
