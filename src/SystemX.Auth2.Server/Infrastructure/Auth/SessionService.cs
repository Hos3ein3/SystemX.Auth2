using OpenIddict.Abstractions;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// Lists and revokes the sessions (OpenIddict authorizations and tokens) of a user in one application.
/// Other applications are not touched, because OpenIddict stores the client of every token.
/// </summary>
public sealed class SessionService(IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations, IAuditService audit) : ISessionService
{
    /// <inheritdoc />
    public async Task<int> RevokeAllAsync(AppUser user, Auth2Application application, string reason, string? keepAuthorizationId = null, CancellationToken ct = default)
    {
        if (application.OpenIddictApplicationId is not { } clientId) return 0;
        var subject = user.Id.ToString("D");
        var client = clientId.ToString("D");

        var count = 0;
        await foreach (var token in tokens.FindBySubjectAsync(subject, ct))
        {
            if (await tokens.GetApplicationIdAsync(token, ct) != client) continue;
            if (keepAuthorizationId is not null && await tokens.GetAuthorizationIdAsync(token, ct) == keepAuthorizationId) continue;
            if (await tokens.TryRevokeAsync(token, ct)) count++;
        }
        await foreach (var authorization in authorizations.FindBySubjectAsync(subject, ct))
        {
            if (await authorizations.GetApplicationIdAsync(authorization, ct) != client) continue;
            if (keepAuthorizationId is not null && await authorizations.GetIdAsync(authorization, ct) == keepAuthorizationId) continue;
            await authorizations.TryRevokeAsync(authorization, ct);
        }

        await audit.WriteAsync(new AuditEntry(reason, application.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["revokedTokens"] = count }), ct);
        return count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionInfo>> ListAsync(AppUser user, Auth2Application application, CancellationToken ct = default)
    {
        var result = new List<SessionInfo>();
        if (application.OpenIddictApplicationId is not { } clientId) return result;
        var client = clientId.ToString("D");
        await foreach (var a in authorizations.FindBySubjectAsync(user.Id.ToString("D"), ct))
        {
            if (await authorizations.GetApplicationIdAsync(a, ct) != client) continue;
            if (await authorizations.GetStatusAsync(a, ct) != OpenIddictConstants.Statuses.Valid) continue;
            result.Add(new SessionInfo((await authorizations.GetIdAsync(a, ct))!, await authorizations.GetCreationDateAsync(a, ct),
                [.. await authorizations.GetScopesAsync(a, ct)]));
        }
        return result.OrderByDescending(s => s.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(AppUser user, Auth2Application application, string authorizationId, CancellationToken ct = default)
    {
        var authorization = await authorizations.FindByIdAsync(authorizationId, ct);
        if (authorization is null) return false;
        // It must be this user's authorization in this application. Otherwise the answer is the same as "not found".
        if (await authorizations.GetSubjectAsync(authorization, ct) != user.Id.ToString("D")
            || application.OpenIddictApplicationId is not { } clientId
            || await authorizations.GetApplicationIdAsync(authorization, ct) != clientId.ToString("D")) return false;

        await foreach (var token in tokens.FindByAuthorizationIdAsync(authorizationId, ct)) await tokens.TryRevokeAsync(token, ct);
        await authorizations.TryRevokeAsync(authorization, ct);
        await audit.WriteAsync(new AuditEntry("session.revoked", application.Id, ActorUserId: user.Id, TargetType: "session", TargetId: authorizationId), ct);
        return true;
    }
}
