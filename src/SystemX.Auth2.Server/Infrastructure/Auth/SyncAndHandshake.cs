using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
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
        var app = await flow.GetApplicationAsync(ct);
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

    private async Task<SyncOutcome> ApplyAsync(Auth2Application app, bool markMissingObsolete, Dictionary<string, PermissionDefinition> wanted, CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, ct);
        int added = 0, updated = 0, unchanged = 0;
        var addedKeys = new List<string>();

        foreach (var (key, def) in wanted)
        {
            if (!existing.TryGetValue(key, out var p))
            {
                db.Permissions.Add(new Permission { ApplicationId = app.Id, Key = key, DisplayName = def.DisplayName, Description = def.Description, Group = def.Group, Source = PermissionSource.Sync });
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
        var app = await flow.GetApplicationAsync(ct);
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

    private async Task<SyncOutcome> ApplyAsync(Auth2Application app, bool permissionSyncAllowed, Dictionary<string, (RoleDefinition Def, List<string> Keys)> wanted, CancellationToken ct)
    {
        // Permissions named by roles but unknown here are created as sync permissions, only when permission sync is allowed.
        var allKeys = wanted.Values.SelectMany(w => w.Keys).Distinct().ToList();
        var known = await db.Permissions.Where(p => allKeys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, ct);
        var unknown = allKeys.Where(k => !known.ContainsKey(k)).ToList();
        if (unknown.Count > 0 && !permissionSyncAllowed) return Fail(Auth2ErrorCodes.SyncNotAllowed);

        int added = 0, updated = 0, unchanged = 0, linksChanged = 0;
        var addedNames = new List<string>();
        foreach (var key in unknown)
        {
            var p = new Permission { ApplicationId = app.Id, Key = key, Source = PermissionSource.Sync };
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
            var role = await db.Roles.FirstOrDefaultAsync(r => r.ApplicationId == app.Id && r.NormalizedName == normalized, ct);   // own roles only
            var created = false;
            if (role is null)
            {
                role = new AppRole(name) { ApplicationId = app.Id, Description = def.Description };
                var result = await roles.CreateAsync(role);
                if (!result.Succeeded)
                {
                    // Lost a race with a parallel sync: the role exists now.
                    db.ChangeTracker.Clear();
                    role = await db.Roles.FirstOrDefaultAsync(r => r.ApplicationId == app.Id && r.NormalizedName == normalized, ct);
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

        if (linksChanged > 0) await versions.BumpApplicationAsync(ct);   // role permissions changed: users' access may change
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

internal sealed class ClientHandshakeService(IAuthFlowResolver flow, IOptions<Auth2ServerOptions> options, IServerUrls urls) : IClientHandshakeService
{
    public async Task<(OperationResult Result, ClientHandshakeResponse? Response)> CheckAsync(ClientHandshakeRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);   // from the token; the body only gets compared
        var settings = await flow.GetSettingsAsync(ct);
        var multi = options.Value.Mode == ServerMode.MultiApplication;
        var hasKey = !string.IsNullOrWhiteSpace(request?.ProjectKey);

        static (OperationResult, ClientHandshakeResponse?) Reject(string code)
        {
            Operations.Auth2Metrics.Handshakes.Add(1, new KeyValuePair<string, object?>("result", code));
            return (OperationResult.Fail(code), null);
        }
        if (multi && !hasKey) return Reject(Auth2ErrorCodes.ServerModeMismatch);
        if (!multi && hasKey) return Reject(Auth2ErrorCodes.ServerModeMismatch);
        if (multi && !string.Equals(request!.ProjectKey!.Trim(), app.ProjectKey, StringComparison.OrdinalIgnoreCase)) return Reject(Auth2ErrorCodes.ProjectKeyOrNameMismatch);
        if (!string.Equals(request?.ApplicationName?.Trim(), app.Name, StringComparison.OrdinalIgnoreCase)) return Reject(Auth2ErrorCodes.ProjectKeyOrNameMismatch);
        Operations.Auth2Metrics.Handshakes.Add(1, new KeyValuePair<string, object?>("result", "ok"));

        var issuer = urls.Build("", []);
        TimeSpan? cookie = settings.Sessions.CookieLifetimeMinutes is { } m ? TimeSpan.FromMinutes(m) : null;
        return (OperationResult.Ok, new ClientHandshakeResponse(app.Id, multi ? app.ProjectKey : null, app.Name, options.Value.Mode,
            issuer, app.SettingsVersion, cookie, settings.Sessions.SingleActiveSession));
    }
}
