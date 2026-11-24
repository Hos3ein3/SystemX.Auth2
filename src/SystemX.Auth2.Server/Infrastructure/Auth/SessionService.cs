using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// Lists and revokes the sessions (OpenIddict authorizations and tokens) of a user in one project: the sessions of all applications of the project.
/// Other projects are not touched, because OpenIddict stores the client of every token and every client belongs to one project.
/// </summary>
public sealed class SessionService(IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations, IAuditService audit, Persistence.Auth2DbContext db) : ISessionService
{
    private async Task<HashSet<string>> ClientIdsAsync(Project project, CancellationToken ct) =>
        [.. (await db.Set<OpenIddict.Auth2Application>().AsNoTracking().Where(a => a.ProjectId == project.Id).Select(a => a.Id).ToListAsync(ct)).Select(id => id.ToString("D"))];

    /// <inheritdoc />
    public async Task<int> RevokeAllAsync(AppUser user, Project application, string reason, string? keepAuthorizationId = null, CancellationToken ct = default)
    {
        var subject = user.Id.ToString("D");
        var clients = await ClientIdsAsync(application, ct);
        if (clients.Count == 0) return 0;

        var count = 0;
        await foreach (var token in tokens.FindBySubjectAsync(subject, ct))
        {
            if (!clients.Contains(await tokens.GetApplicationIdAsync(token, ct) ?? "")) continue;
            if (keepAuthorizationId is not null && await tokens.GetAuthorizationIdAsync(token, ct) == keepAuthorizationId) continue;
            if (await tokens.TryRevokeAsync(token, ct)) count++;
        }
        await foreach (var authorization in authorizations.FindBySubjectAsync(subject, ct))
        {
            if (!clients.Contains(await authorizations.GetApplicationIdAsync(authorization, ct) ?? "")) continue;
            if (keepAuthorizationId is not null && await authorizations.GetIdAsync(authorization, ct) == keepAuthorizationId) continue;
            await authorizations.TryRevokeAsync(authorization, ct);
        }

        await audit.WriteAsync(new AuditEntry(reason, application.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["revokedTokens"] = count }), ct);
        return count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionInfo>> ListAsync(AppUser user, Project application, CancellationToken ct = default)
    {
        var result = new List<SessionInfo>();
        var clients = await ClientIdsAsync(application, ct);
        await foreach (var a in authorizations.FindBySubjectAsync(user.Id.ToString("D"), ct))
        {
            if (!clients.Contains(await authorizations.GetApplicationIdAsync(a, ct) ?? "")) continue;
            if (await authorizations.GetStatusAsync(a, ct) != OpenIddictConstants.Statuses.Valid) continue;
            result.Add(new SessionInfo((await authorizations.GetIdAsync(a, ct))!, await authorizations.GetCreationDateAsync(a, ct),
                [.. await authorizations.GetScopesAsync(a, ct)]));
        }
        return result.OrderByDescending(s => s.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(AppUser user, Project application, string authorizationId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(authorizationId, out _)) return false;   // not an id of ours: same answer as "not found"
        var authorization = await authorizations.FindByIdAsync(authorizationId, ct);
        if (authorization is null) return false;
        // It must be this user's authorization in this application. Otherwise the answer is the same as "not found".
        if (await authorizations.GetSubjectAsync(authorization, ct) != user.Id.ToString("D")
            || !(await ClientIdsAsync(application, ct)).Contains(await authorizations.GetApplicationIdAsync(authorization, ct) ?? "")) return false;

        await foreach (var token in tokens.FindByAuthorizationIdAsync(authorizationId, ct)) await tokens.TryRevokeAsync(token, ct);
        await authorizations.TryRevokeAsync(authorization, ct);
        await audit.WriteAsync(new AuditEntry("session.revoked", application.Id, ActorUserId: user.Id, TargetType: "session", TargetId: authorizationId), ct);
        return true;
    }
}
