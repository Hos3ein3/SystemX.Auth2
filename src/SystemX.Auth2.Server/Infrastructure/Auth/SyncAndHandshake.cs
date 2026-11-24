using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

internal sealed class PermissionSyncService(Auth2DbContext db, IAuthFlowResolver flow, IAuditService audit) : IPermissionSyncService
{
    public const int MaxItems = 2000;

    public async Task<SyncOutcome> SyncAsync(PermissionSyncRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        if (!settings.Clients.PermissionSyncAllowed) return Fail(Auth2ErrorCodes.SyncNotAllowed);
        var items = request?.Permissions ?? [];
        if (items.Count > MaxItems) return Fail(Auth2ErrorCodes.TooManyItems);

        // Validate everything first, so a bad list changes nothing. Same key twice: the first one wins.
        var wanted = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = AccessKeys.NormalizePermissionKey(item?.Key);
            if (key is null) return Fail(Auth2ErrorCodes.InvalidPermissionKey);
            wanted.TryAdd(key, item!);
        }

        // A parallel sync can insert the same key between our read and our save. The unique index stops it; we read again once.
        for (var attempt = 0; ; attempt++)
        {
            try { return await AccessSupport.SerializedAsync(db, app.Id, () => ApplyAsync(app, settings.Clients.AllowRemovePermissionsFromSync, wanted, ct), ct); }
            catch (Exception ex) when (attempt == 0 && AccessSupport.IsUniqueViolation(ex)) { db.ChangeTracker.Clear(); }
        }
    }

    private async Task<SyncOutcome> ApplyAsync(Project app, bool markMissingObsolete, Dictionary<string, PermissionDefinition> wanted, CancellationToken ct)
    {
        var sourceApplicationId = (await flow.GetClientAsync(ct))?.Id;   // the client that pushes it (kept on the item)
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, ct);
        int added = 0, updated = 0, unchanged = 0;
        var addedKeys = new List<string>();

        foreach (var (key, def) in wanted)
        {
            if (!existing.TryGetValue(key, out var p))
            {
                db.Permissions.Add(new Permission { ProjectId = app.Id, ApplicationId = sourceApplicationId, Key = key, DisplayName = def.DisplayName, Description = def.Description, Group = def.Group, Source = PermissionSource.Sync });
                added++; addedKeys.Add(key);
                continue;
            }
            var changed = false;
            if (p.IsObsolete) { p.IsObsolete = false; changed = true; }   // the client declares it again
            if (p.Source == PermissionSource.Sync)   // an admin's text is never overwritten
            {
                if (def.DisplayName is not null && def.DisplayName != p.DisplayName) { p.DisplayName = def.DisplayName; changed = true; }
                if (def.Description is not null && def.Description != p.Description) { p.Description = def.Description; changed = true; }
                if (def.Group is not null && def.Group != p.Group) { p.Group = def.Group; changed = true; }
            }
            if (changed) updated++; else unchanged++;
        }

        // Opt-in: permissions that came from sync and are no longer declared are marked obsolete. They are never deleted.
        if (markMissingObsolete)
            foreach (var p in existing.Values.Where(p => p.Source == PermissionSource.Sync && !p.IsObsolete && !wanted.ContainsKey(p.Key)))
            { p.IsObsolete = true; updated++; }

        if (added + updated > 0)
        {
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("permissions.synced", app.Id, TargetType: "application", TargetId: app.Id.ToString(),
                Data: new Dictionary<string, object?> { ["added"] = added, ["updated"] = updated }), ct);
        }
        addedKeys.Sort(StringComparer.Ordinal);
        Operations.Auth2Metrics.SyncChanges.Add(added + updated, new KeyValuePair<string, object?>("kind", "permissions"));
        return new SyncOutcome(OperationResult.Ok, new SyncResponse(added, updated, unchanged, addedKeys));
    }

    private static SyncOutcome Fail(string code) => new(OperationResult.Fail(code), null);
}

internal sealed class RoleSyncService(Auth2DbContext db, IAuthFlowResolver flow, RoleManager<AppRole> roles, IAccessVersionService versions, IAuditService audit) : IRoleSyncService
{
    public const int MaxItems = 2000;
    private const int MaxNameLength = 100;

    public async Task<SyncOutcome> SyncAsync(RoleSyncRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        if (!settings.Clients.RoleSyncAllowed) return Fail(Auth2ErrorCodes.SyncNotAllowed);
        var items = request?.Roles ?? [];
        if (items.Count > MaxItems) return Fail(Auth2ErrorCodes.TooManyItems);

        var wanted = new Dictionary<string, (RoleDefinition Def, List<string> Keys)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var name = item?.Name?.Trim();
            if (!IsValidRoleName(name)) return Fail(Auth2ErrorCodes.ValidationFailed);
            var keys = new List<string>();
            foreach (var raw in item!.Permissions ?? [])
            {
                var key = AccessKeys.NormalizePermissionKey(raw);
                if (key is null) return Fail(Auth2ErrorCodes.InvalidPermissionKey);
                if (!keys.Contains(key)) keys.Add(key);
            }
            wanted.TryAdd(name!, (item, keys));
        }

        return await AccessSupport.SerializedAsync(db, app.Id, () => ApplyAsync(app, settings.Clients.PermissionSyncAllowed, wanted, ct), ct);
    }

    private async Task<SyncOutcome> ApplyAsync(Project app, bool permissionSyncAllowed, Dictionary<string, (RoleDefinition Def, List<string> Keys)> wanted, CancellationToken ct)
    {
        var sourceApplicationId = (await flow.GetClientAsync(ct))?.Id;   // the client that pushes it (kept on the item)
        // Permissions named by roles but unknown here are created as sync permissions, only when permission sync is allowed.
        var allKeys = wanted.Values.SelectMany(w => w.Keys).Distinct().ToList();
        var known = await db.Permissions.Where(p => allKeys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, ct);
        var unknown = allKeys.Where(k => !known.ContainsKey(k)).ToList();
        if (unknown.Count > 0 && !permissionSyncAllowed) return Fail(Auth2ErrorCodes.SyncNotAllowed);

        int added = 0, updated = 0, unchanged = 0, linksChanged = 0;
        var addedNames = new List<string>();
        foreach (var key in unknown)
        {
            var p = new Permission { ProjectId = app.Id, ApplicationId = sourceApplicationId, Key = key, Source = PermissionSource.Sync };
            db.Permissions.Add(p);
            known[key] = p;
        }
        try { if (unknown.Count > 0) await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (AccessSupport.IsUniqueViolation(ex))   // a parallel sync made them: read them again
        {
            db.ChangeTracker.Clear();
            known = await db.Permissions.Where(p => allKeys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, ct);
        }

        foreach (var (name, (def, keys)) in wanted)
        {
            var normalized = roles.NormalizeKey(name);
            var role = await db.Roles.FirstOrDefaultAsync(r => r.ProjectId == app.Id && r.NormalizedName == normalized, ct);   // own roles only
            var created = false;
            if (role is null)
            {
                role = new AppRole(name) { ProjectId = app.Id, ApplicationId = sourceApplicationId, Description = def.Description };
                var result = await roles.CreateAsync(role);
                if (!result.Succeeded)
                {
                    // Lost a race with a parallel sync: the role exists now.
                    db.ChangeTracker.Clear();
                    role = await db.Roles.FirstOrDefaultAsync(r => r.ProjectId == app.Id && r.NormalizedName == normalized, ct);
                    if (role is null) return Fail(Auth2ErrorCodes.ValidationFailed);
                }
                else { created = true; added++; addedNames.Add(name); }
            }

            var changed = false;
            if (!created && !role.IsSystem && def.Description is not null && def.Description != role.Description)
            {
                await db.Roles.Where(r => r.Id == role.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Description, def.Description), ct);
                changed = true;
            }
            if (def.Permissions is not null)
            {
                var linked = await db.RolePermissions.Where(rp => rp.RoleId == role.Id).Select(rp => rp.PermissionId).ToListAsync(ct);
                var newLinks = keys.Where(k => known.ContainsKey(k) && !linked.Contains(known[k].Id)).ToList();
                foreach (var k in newLinks) db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = known[k].Id });
                if (newLinks.Count > 0)
                {
                    try { await db.SaveChangesAsync(ct); linksChanged += newLinks.Count; changed = true; }
                    catch (Exception ex) when (AccessSupport.IsUniqueViolation(ex)) { db.ChangeTracker.Clear(); }
                }
            }
            if (!created) { if (changed) updated++; else unchanged++; }
        }

        if (linksChanged > 0) await versions.BumpProjectAsync(ct);   // role permissions changed: users' access may change
        if (added + updated > 0 || unknown.Count > 0)
            await audit.WriteAsync(new AuditEntry("roles.synced", app.Id, TargetType: "application", TargetId: app.Id.ToString(),
                Data: new Dictionary<string, object?> { ["added"] = added, ["updated"] = updated, ["permissionsCreated"] = unknown.Count }), ct);
        addedNames.Sort(StringComparer.Ordinal);
        Operations.Auth2Metrics.SyncChanges.Add(added + updated, new KeyValuePair<string, object?>("kind", "roles"));
        return new SyncOutcome(OperationResult.Ok, new SyncResponse(added, updated, unchanged, addedNames));
    }

    /// <summary>1–100 letters, digits, space or <c>. _ : -</c>. The built-in admin role names are reserved.</summary>
    private static bool IsValidRoleName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        if (name.Equals(Auth2Roles.GlobalAdmin, StringComparison.OrdinalIgnoreCase) || name.Equals(Auth2Roles.ProjectAdmin, StringComparison.OrdinalIgnoreCase)) return false;
        return name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '_' or ':' or '-');
    }

    private static SyncOutcome Fail(string code) => new(OperationResult.Fail(code), null);
}

internal sealed class ClientHandshakeService(IAuthFlowResolver flow, IOptions<Auth2ServerOptions> options, IServerUrls urls, ILogger<ClientHandshakeService> log) : IClientHandshakeService
{
    public async Task<(OperationResult Result, ClientHandshakeResponse? Response)> CheckAsync(ClientHandshakeRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);   // from the token (the application knows its project); the body only gets compared
        var client = await flow.GetClientAsync(ct) ?? throw new ProjectNotFoundException("No application matches this request.");
        var settings = await flow.GetEffectiveAsync(ct);
        var multi = options.Value.Mode == ServerMode.MultiProject;
        var hasKey = !string.IsNullOrWhiteSpace(request?.ProjectKey);

        static (OperationResult, ClientHandshakeResponse?) Reject(string code)
        {
            Operations.Auth2Metrics.Handshakes.Add(1, new KeyValuePair<string, object?>("result", code));
            return (OperationResult.Fail(code), null);
        }
        if (multi && !hasKey) return Reject(Auth2ErrorCodes.ServerModeMismatch);
        if (!multi && hasKey)
            log.LogWarning("The client {ClientId} sent a ProjectKey, but this server has a single project. The key is ignored. Remove ServerWithMultiProject(...) from the client.", client.ClientId);   // not an error: the key is only an identifier check
        if (multi && !string.Equals(request!.ProjectKey!.Trim(), app.ProjectKey, StringComparison.OrdinalIgnoreCase)) return Reject(Auth2ErrorCodes.ProjectKeyOrNameMismatch);   // the application does not belong to that project
        if (!string.Equals(request?.ApplicationName?.Trim(), client.Name, StringComparison.OrdinalIgnoreCase)) return Reject(Auth2ErrorCodes.ProjectKeyOrNameMismatch);
        Operations.Auth2Metrics.Handshakes.Add(1, new KeyValuePair<string, object?>("result", "ok"));

        var issuer = urls.Build("", []);
        TimeSpan? cookie = settings.CookieLifetimeMinutes is { } m ? TimeSpan.FromMinutes(m) : null;
        return (OperationResult.Ok, new ClientHandshakeResponse(app.Id, client.Id, multi ? app.ProjectKey : null, client.Name, options.Value.Mode,
            issuer, app.SettingsVersion, cookie, settings.Project.Sessions.SingleActiveSession));
    }
}

internal sealed class GroupSyncService(Auth2DbContext db, IAuthFlowResolver flow, IAuditService audit) : IGroupSyncService
{
    public const int MaxItems = 2000;

    public async Task<SyncOutcome> SyncAsync(GroupSyncRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        if (!settings.Clients.GroupSyncAllowed) return Fail(Auth2ErrorCodes.SyncNotAllowed);

        var items = request?.Groups ?? [.. (request?.Names ?? []).Select(n => new Contracts.Access.GroupDefinition(n))];
        if (items.Count > MaxItems) return Fail(Auth2ErrorCodes.TooManyItems);
        var wanted = new Dictionary<string, Contracts.Access.GroupDefinition>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var name = AccessKeys.NormalizeGroupName(item?.Name);
            if (name is null) return Fail(Auth2ErrorCodes.ValidationFailed);
            wanted.TryAdd(name, item!);
        }

        for (var attempt = 0; ; attempt++)
        {
            try { return await AccessSupport.SerializedAsync(db, app.Id, () => ApplyAsync(app, wanted, ct), ct); }
            catch (Exception ex) when (attempt == 0 && AccessSupport.IsUniqueViolation(ex)) { db.ChangeTracker.Clear(); }
        }
    }

    private async Task<SyncOutcome> ApplyAsync(Project app, Dictionary<string, Contracts.Access.GroupDefinition> wanted, CancellationToken ct)
    {
        var sourceApplicationId = (await flow.GetClientAsync(ct))?.Id;   // the client that pushes it (kept on the item)
        var existing = await db.Groups.Select(g => g.Name).ToListAsync(ct);
        var have = new HashSet<string>(existing, StringComparer.Ordinal);
        var addedKeys = new List<string>();
        foreach (var (name, def) in wanted)
        {
            if (have.Contains(name)) continue;
            // New groups only. The display name of an existing group is the admin's and is never changed by sync.
            db.Groups.Add(new Group { ProjectId = app.Id, ApplicationId = sourceApplicationId, Name = name, DisplayName = def.DisplayName });
            addedKeys.Add(name);
        }
        if (addedKeys.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEntry("groups.synced", app.Id, TargetType: "application", TargetId: app.Id.ToString(),
                Data: new Dictionary<string, object?> { ["added"] = addedKeys.Count }), ct);
        }
        addedKeys.Sort(StringComparer.Ordinal);
        Operations.Auth2Metrics.SyncChanges.Add(addedKeys.Count, new KeyValuePair<string, object?>("kind", "groups"));
        return new SyncOutcome(OperationResult.Ok, new SyncResponse(addedKeys.Count, 0, wanted.Count - addedKeys.Count, addedKeys));
    }

    private static SyncOutcome Fail(string code) => new(OperationResult.Fail(code), null);
}

internal sealed class ScopeSyncService(IServiceScopeFactory scopeFactory, IAuthFlowResolver flow, IOpenIddictScopeManager scopes,
    IDistributedLock locks, IAuditService audit) : IScopeSyncService
{
    public const int MaxItems = 500;

    public async Task<SyncOutcome> SyncAsync(ScopeSyncRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var client = await flow.GetClientAsync(ct) ?? throw new ProjectNotFoundException("No application matches this request.");
        var settings = await flow.GetSettingsAsync(ct);
        if (!settings.Clients.ScopeSyncAllowed) return Fail(Auth2ErrorCodes.SyncNotAllowed);
        var items = request?.Scopes ?? [];
        if (items.Count > MaxItems) return Fail(Auth2ErrorCodes.TooManyItems);

        var wanted = new Dictionary<string, Contracts.Access.ScopeDefinition>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var name = AccessKeys.NormalizeScopeName(item?.Name);
            if (name is null) return Fail(Auth2ErrorCodes.InvalidScopeName);
            wanted.TryAdd(name, item!);
        }

        var added = new List<string>();
        // One at a time per application: two syncs must not both read the settings and overwrite each other's additions.
        await locks.RunExclusiveAsync($"scopes:{client.Id:N}", async token =>
        {
            foreach (var (name, def) in wanted)
            {
                if (await scopes.FindByNameAsync(name, token) is not null) continue;   // the registry is shared; the first text stays
                try
                {
                    await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = name, DisplayName = def.DisplayName, Description = def.Description }, token);
                }
                catch (Exception)
                {
                    if (await scopes.FindByNameAsync(name, token) is null) throw;   // not a race: a real error
                }
            }

            // A new DI scope: its own DbContext, so no old copy of the OpenIddict client is tracked.
            // The allowed scopes are a field of the OpenIddict application: the calling application gets the permission for the new scopes.
            await using var inner = scopeFactory.CreateAsyncScope();
            var applications = inner.ServiceProvider.GetRequiredService<IApplicationService>();
            var current = await applications.FindAsync(client.Id, token) ?? throw new ProjectNotFoundException("Application not found.");
            var missing = wanted.Keys.Where(n => !current.AllowedScopes.Contains(n, StringComparer.Ordinal)).ToList();
            if (missing.Count == 0) return;
            await applications.UpdateAsync(client.Id, new ApplicationSpec("", AllowedScopes: [.. current.AllowedScopes, .. missing]), token);
            added.AddRange(missing);
        }, ct);

        if (added.Count > 0)
            await audit.WriteAsync(new AuditEntry("scopes.synced", app.Id, ActorClientId: client.ClientId, TargetType: "application", TargetId: client.Id.ToString(),
                Data: new Dictionary<string, object?> { ["added"] = added.Count }), ct);
        added.Sort(StringComparer.Ordinal);
        Operations.Auth2Metrics.SyncChanges.Add(added.Count, new KeyValuePair<string, object?>("kind", "scopes"));
        return new SyncOutcome(OperationResult.Ok, new SyncResponse(added.Count, 0, wanted.Count - added.Count, added));
    }

    public async Task<IReadOnlyList<ScopeDto>> ListAsync(CancellationToken ct = default)
    {
        var client = await flow.GetClientAsync(ct) ?? throw new ProjectNotFoundException("No application matches this request.");
        var result = new List<ScopeDto>();
        foreach (var name in client.AllowedScopes.Where(n => AccessKeys.NormalizeScopeName(n) == n).Order(StringComparer.Ordinal))
        {
            var scope = await scopes.FindByNameAsync(name, ct);
            result.Add(new ScopeDto(name, scope is null ? null : await scopes.GetDisplayNameAsync(scope, ct), scope is null ? null : await scopes.GetDescriptionAsync(scope, ct)));
        }
        return result;
    }

    private static SyncOutcome Fail(string code) => new(OperationResult.Fail(code), null);
}
