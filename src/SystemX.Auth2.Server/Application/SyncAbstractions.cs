using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Applications;

namespace SystemX.Auth2.Server.Application;

/// <summary>Result of a sync call.</summary>
/// <param name="Result">Success or the error.</param>
/// <param name="Response">The counts when it worked.</param>
public sealed record SyncOutcome(OperationResult Result, SyncResponse? Response);

/// <summary>
/// Idempotent upsert of permissions sent by a client application (current application only).
/// Never deletes. Allowed only when <c>PermissionSyncAllowed</c> is on.
/// </summary>
public interface IPermissionSyncService
{
    /// <summary>Syncs a list. All-or-nothing: one invalid key rejects the whole request.</summary>
    Task<SyncOutcome> SyncAsync(PermissionSyncRequest request, CancellationToken ct = default);
}

/// <summary>Idempotent upsert of roles (and permissions of roles) sent by a client application. Allowed only when <c>RoleSyncAllowed</c> is on.</summary>
public interface IRoleSyncService
{
    /// <summary>Syncs a list. All-or-nothing validation, then one upsert per role.</summary>
    Task<SyncOutcome> SyncAsync(RoleSyncRequest request, CancellationToken ct = default);
}

/// <summary>The startup check a client makes. The application comes from the token; the request is only compared with it.</summary>
public interface IClientHandshakeService
{
    /// <summary>Checks project key, application name and server mode against the token's application.</summary>
    Task<(OperationResult Result, ClientHandshakeResponse? Response)> CheckAsync(ClientHandshakeRequest request, CancellationToken ct = default);
}

/// <summary>
/// Idempotent add of the groups a client sends (current application). Needs <c>Clients.GroupSyncAllowed</c> (off by default).
/// Only missing groups are created; display names of existing groups are not changed; nothing is deleted.
/// </summary>
public interface IGroupSyncService
{
    /// <summary>Syncs a list. Group names follow the rules of permission keys (lowercase, 1–100 characters).</summary>
    Task<SyncOutcome> SyncAsync(GroupSyncRequest request, CancellationToken ct = default);
}

/// <summary>
/// Custom scopes of an application. A sync registers the scope (when it is new) and allows it for the application's client.
/// Names starting with <c>auth2.</c> and the standard OpenID Connect scopes are reserved. Never removes a scope.
/// </summary>
public interface IScopeSyncService
{
    /// <summary>Syncs a list. <c>Added</c> counts scopes newly allowed for this application.</summary>
    Task<SyncOutcome> SyncAsync(ScopeSyncRequest request, CancellationToken ct = default);

    /// <summary>The custom scopes this application allows.</summary>
    Task<IReadOnlyList<ScopeDto>> ListAsync(CancellationToken ct = default);
}
