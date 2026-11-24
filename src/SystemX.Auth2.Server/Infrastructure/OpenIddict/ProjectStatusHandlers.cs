using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Infrastructure.OpenIddict;

/// <summary>
/// Refuses a token whose project or application was disabled after the token was issued. It runs after OpenIddict has validated the token
/// (signature, lifetime, revocation) in the server (authorization code, refresh token, userinfo, introspection) and in the validation
/// stack (the server's own APIs). The answer comes from <see cref="IProjectStatusCache"/>, so a change counts after the cache window
/// (<c>Auth2:ProjectStatusCacheSeconds</c>) at the latest. The public answer is generic; nothing about the reason leaves the server.
/// </summary>
internal static class ProjectStatusCheck
{
    public static async ValueTask<bool> IsBlockedAsync(System.Security.Claims.ClaimsPrincipal? principal, IProjectStatusCache status, CancellationToken ct)
    {
        if (principal is null) return false;
        if (Guid.TryParse(principal.GetClaim(Auth2Claims.ProjectId), out var projectId) && !await status.IsProjectEnabledAsync(projectId, ct)) return true;
        // The application of a user token is the audience, the one of a machine token is its subject (and audience too).
        var clientId = principal.GetAudiences().FirstOrDefault();
        return !string.IsNullOrEmpty(clientId) && !await status.IsApplicationEnabledAsync(clientId, ct);
    }
}

/// <summary>The check for the OpenIddict server events.</summary>
internal sealed class ProjectStatusServerHandler(IProjectStatusCache status) : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessAuthenticationContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor
        .CreateBuilder<OpenIddictServerEvents.ProcessAuthenticationContext>()
        .UseScopedHandler<ProjectStatusServerHandler>()
        .SetOrder(int.MaxValue - 100_000)
        .SetType(OpenIddictServerHandlerType.Custom)
        .Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessAuthenticationContext context)
    {
        if (context.IsRejected) return;
        // Whatever token the request brought (code, refresh token, access token, ...): one of them is set.
        foreach (var principal in new[] { context.AuthorizationCodePrincipal, context.RefreshTokenPrincipal, context.AccessTokenPrincipal, context.GenericTokenPrincipal })
        {
            if (principal is null || !await ProjectStatusCheck.IsBlockedAsync(principal, status, context.CancellationToken)) continue;
            context.Reject(Errors.InvalidToken, "The token is not valid anymore.");
            return;
        }
    }
}

/// <summary>The check for the OpenIddict validation events.</summary>
internal sealed class ProjectStatusValidationHandler(IProjectStatusCache status) : IOpenIddictValidationHandler<OpenIddictValidationEvents.ProcessAuthenticationContext>
{
    public static OpenIddictValidationHandlerDescriptor Descriptor { get; } = OpenIddictValidationHandlerDescriptor
        .CreateBuilder<OpenIddictValidationEvents.ProcessAuthenticationContext>()
        .UseScopedHandler<ProjectStatusValidationHandler>()
        .SetOrder(int.MaxValue - 100_000)
        .SetType(OpenIddictValidationHandlerType.Custom)
        .Build();

    public async ValueTask HandleAsync(OpenIddictValidationEvents.ProcessAuthenticationContext context)
    {
        if (context.IsRejected || context.AccessTokenPrincipal is null) return;
        if (await ProjectStatusCheck.IsBlockedAsync(context.AccessTokenPrincipal, status, context.CancellationToken))
            context.Reject(Errors.InvalidToken, "The token is not valid anymore.");
    }
}
