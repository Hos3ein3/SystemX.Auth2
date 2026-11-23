using OpenIddict.Abstractions;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// Revokes the tokens and authorizations of a user in one application. Other applications are not touched,
/// because OpenIddict stores the client of every token.
/// </summary>
public sealed class SessionService(IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations, IAuditService audit) : ISessionService
{
    /// <inheritdoc />
    public async Task<int> RevokeAllAsync(AppUser user, Auth2Application application, string reason, CancellationToken ct = default)
    {
        if (application.OpenIddictApplicationId is not { } clientId) return 0;
        var subject = user.Id.ToString("D");
        var client = clientId.ToString("D");

        var count = 0;
        // The client of a token is the OpenIddict application of THIS application, so other applications are not touched.
        await foreach (var token in tokens.FindBySubjectAsync(subject, ct))
            if (await tokens.GetApplicationIdAsync(token, ct) == client && await tokens.TryRevokeAsync(token, ct)) count++;
        await foreach (var authorization in authorizations.FindBySubjectAsync(subject, ct))
            if (await authorizations.GetApplicationIdAsync(authorization, ct) == client)
                await authorizations.TryRevokeAsync(authorization, ct);

        await audit.WriteAsync(new AuditEntry(reason, application.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["revokedTokens"] = count }), ct);
        return count;
    }
}
