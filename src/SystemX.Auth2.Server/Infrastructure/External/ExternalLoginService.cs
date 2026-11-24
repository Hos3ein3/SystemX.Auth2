using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Server.Resources;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>See <see cref="IExternalLoginService"/>. One decision table for every kind of external source.</summary>
internal sealed class ExternalLoginService(
    UserManager<AppUser> users, Auth2DbContext db, IAuthFlowResolver flow, ILoginService login, IExternalMappingService mapping,
    IExternalProviderDescriptors descriptors, IExternalProviderRegistry registry, IAuditService audit, IDataProtectionProvider protection,
    IOptions<Auth2ServerOptions> options, IEmailSender email, IServerUrls urls, IStringLocalizer<Emails> text, TimeProvider clock) : IExternalLoginService
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(30);
    private ITimeLimitedDataProtector Pending => protection.CreateProtector("SystemX.Auth2.ExternalPending.v1").ToTimeLimitedDataProtector();
    private ITimeLimitedDataProtector Confirm => protection.CreateProtector("SystemX.Auth2.ExternalEmailConfirm.v1").ToTimeLimitedDataProtector();

    private sealed record PendingIdentity(string Provider, string Subject, string? Issuer, string? FirstName, string? LastName, string? DisplayName, string? Phone, string[] Groups, string ReturnUrl, bool Directory);

    /// <inheritdoc />
    public async Task<ExternalLoginOutcome> HandleAsync(ExternalLoginRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var id = request.Identity;
        if (!app.IsEnabled) return await RefuseAsync(app, request, Auth2ErrorCodes.ApplicationDisabled, "application_disabled", ct);
        if (string.IsNullOrWhiteSpace(id.Subject)) return await RefuseAsync(app, request, Auth2ErrorCodes.ExternalClaimsInvalid, "subject_missing", ct);
        var current = registry.Get(app.Id, request.Provider.Name);
        if (current is null) return await RefuseAsync(app, request, Auth2ErrorCodes.ExternalProviderDisabled, "provider_disabled", ct);
        return await DecideAsync(app, request with { Provider = current }, emailProven: false, ct);
    }

    private async Task<ExternalLoginOutcome> DecideAsync(Auth2Application app, ExternalLoginRequest request, bool emailProven, CancellationToken ct)
    {
        var settings = await flow.GetSettingsAsync(ct);
        var p = request.Provider;
        var s = p.Settings;
        var id = request.Identity;
        var email = string.IsNullOrWhiteSpace(id.Email) ? null : id.Email.Trim();
        var trusted = emailProven || (email is not null && (s.TrustProviderEmailVerification ?? descriptors.For(s.Type).EmailVerifiedByDefault) && id.EmailVerified == true);

        var registered = false; var linked = false;
        var user = await users.FindByLoginAsync(p.Name, id.Subject);
        if (user is null)
        {
            var existing = email is null ? null : await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                if (!(trusted && s.AllowLink)) return await RefuseAsync(app, request, Auth2ErrorCodes.ExternalEmailExistsNotVerified, trusted ? "link_not_allowed" : "email_not_verified", ct);
                await EnsureMembershipAsync(existing, app, ct);
                var add = await users.AddLoginAsync(existing, new UserLoginInfo(p.Name, id.Subject, s.DisplayName ?? p.Name));
                if (!add.Succeeded) return await RefuseAsync(app, request, Auth2ErrorCodes.ExternalClaimsInvalid, "link_failed", ct);
                user = existing; linked = true;
                await audit.WriteAsync(Entry("external.login.linked", app, user, request, new() { ["provider"] = p.Name }), ct);
            }
            else
            {
                if (!s.AllowRegister || settings.Registration.JoinMode != JoinMode.AutoJoin)
                    return await RefuseAsync(app, request, Auth2ErrorCodes.ExternalRegistrationNotAllowed, "registration_not_allowed", ct);
                if (email is null && settings.Email.RequireConfirmedEmail)
                    return new ExternalLoginOutcome { NeedsEmail = true, PendingToken = SealPending(request), ReturnUrl = request.ReturnUrl };
                var created = await CreateUserAsync(app, request, email, trusted, ct);
                if (created is null) return await RefuseAsync(app, request, Auth2ErrorCodes.ExternalClaimsInvalid, "user_create_failed", ct);
                user = created; registered = true;
                await audit.WriteAsync(Entry("external.register", app, user, request, new() { ["provider"] = p.Name }), ct);
            }
        }

        if (!registered && s.ProfileSyncMode == ProfileSyncMode.UpdateOnLogin) await UpdateProfileAsync(user, id);
        if (s.GroupMappingMode == GroupMappingMode.Sync) await mapping.ApplyAsync(app.Id, p.Name, user, id, ct);

        var method = request.IsDirectoryLogin ? LoginMethod.Ldap : LoginMethod.External;
        IReadOnlyList<string> amr = request.IsDirectoryLogin ? ["pwd"] : id.UsedMultiFactor == true ? ["fed", "mfa"] : ["fed"];
        var skip2fa = s.TrustProviderMfa && id.UsedMultiFactor != false;
        var result = await login.ExternalLoginAsync(new ExternalSignInRequest(user, method, p.Name, amr, skip2fa, request.IpAddress, request.UserAgent), ct);
        if (result.Succeeded)
            await audit.WriteAsync(Entry("external.login.success", app, user, request, new() { ["provider"] = p.Name, ["registered"] = registered, ["linked"] = linked }), ct);
        else if (result.ErrorCode != Auth2ErrorCodes.TwoFactorRequired)
            await audit.WriteAsync(Entry(request.IsDirectoryLogin ? "ldap.login.failed" : "external.login.failed", app, user, request, new() { ["provider"] = p.Name, ["code"] = result.ErrorCode }), ct);
        return new ExternalLoginOutcome { Login = result, ErrorCode = result.Succeeded ? null : result.ErrorCode, Registered = registered, Linked = linked, ReturnUrl = request.ReturnUrl };
    }

    // ------------------------------------------------------------------------------- email step

    /// <inheritdoc />
    public async Task<bool> SendEmailConfirmationAsync(string pendingToken, string emailAddress, string confirmUrlBase, CancellationToken ct = default)
    {
        if (ReadPending(pendingToken) is null || !IsEmail(emailAddress)) return false;
        var app = await flow.GetApplicationAsync(ct);
        var token = Confirm.Protect(JsonSerializer.Serialize(new { p = pendingToken, e = emailAddress.Trim() }), PendingLifetime);
        var link = urls.Build(confirmUrlBase, [new("ConfirmationToken", token), new("ClientId", app.ClientId)]);
        var brand = app.Settings.Localization.BrandName ?? app.Name;
        await email.SendAsync(new EmailMessage(emailAddress.Trim(), text["external_confirm_email.subject"].Value,
            text["external_confirm_email.body", brand, link].Value, new Dictionary<string, string> { ["token"] = token, ["link"] = link }), ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<ExternalLoginOutcome> ConfirmEmailAsync(string confirmationToken, CancellationToken ct = default)
    {
        string pending, address;
        try
        {
            using var doc = JsonDocument.Parse(Confirm.Unprotect(confirmationToken));
            pending = doc.RootElement.GetProperty("p").GetString()!;
            address = doc.RootElement.GetProperty("e").GetString()!;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or KeyNotFoundException)
        {
            return new ExternalLoginOutcome { ErrorCode = Auth2ErrorCodes.InvalidToken };
        }
        return await CompleteWithEmailAsync(pending, address, ct, proven: true);
    }

    /// <inheritdoc />
    public Task<ExternalLoginOutcome> CompleteWithEmailAsync(string pendingToken, string emailAddress, CancellationToken ct = default) =>
        CompleteWithEmailAsync(pendingToken, emailAddress, ct, proven: false);

    private async Task<ExternalLoginOutcome> CompleteWithEmailAsync(string pendingToken, string emailAddress, CancellationToken ct, bool proven)
    {
        var app = await flow.GetApplicationAsync(ct);
        var pending = ReadPending(pendingToken);
        if (pending is null || !IsEmail(emailAddress)) return new ExternalLoginOutcome { ErrorCode = Auth2ErrorCodes.InvalidToken };
        var provider = registry.Get(app.Id, pending.Provider);
        if (provider is null) return new ExternalLoginOutcome { ErrorCode = Auth2ErrorCodes.ExternalProviderDisabled };
        var identity = new ExternalIdentity(pending.Provider, pending.Subject, pending.Issuer, emailAddress.Trim(), proven, pending.FirstName, pending.LastName, pending.Phone,
            pending.Groups, new Dictionary<string, string[]>()) { DisplayName = pending.DisplayName };
        return await DecideAsync(app, new ExternalLoginRequest(provider, identity, pending.ReturnUrl, IsDirectoryLogin: pending.Directory), proven, ct);
    }

    private string SealPending(ExternalLoginRequest r) => Pending.Protect(JsonSerializer.Serialize(new PendingIdentity(
        r.Provider.Name, r.Identity.Subject, r.Identity.Issuer, r.Identity.FirstName, r.Identity.LastName, r.Identity.DisplayName, r.Identity.Phone,
        [.. r.Identity.Groups], r.ReturnUrl, r.IsDirectoryLogin)), PendingLifetime);

    private PendingIdentity? ReadPending(string token)
    {
        try { return JsonSerializer.Deserialize<PendingIdentity>(Pending.Unprotect(token)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return null; }
    }

    private static bool IsEmail(string v) => !string.IsNullOrWhiteSpace(v) && v.Length <= 256 && System.Net.Mail.MailAddress.TryCreate(v.Trim(), out _);

    // ------------------------------------------------------------------------------- helpers

    private async Task<AppUser?> CreateUserAsync(Auth2Application app, ExternalLoginRequest request, string? email, bool emailVerified, CancellationToken ct)
    {
        var id = request.Identity;
        var (first, last) = (id.FirstName, id.LastName);
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var user = new AppUser
            {
                UserName = CandidateName(request, email, attempt), Email = email, EmailConfirmed = email is not null && emailVerified,
                FirstName = first, LastName = last, DisplayName = id.DisplayName, PhoneNumber = id.Phone, CreatedAtUtc = clock.GetUtcNow(),
            };
            var result = await users.CreateAsync(user);
            if (result.Succeeded)
            {
                await EnsureMembershipAsync(user, app, ct);
                var add = await users.AddLoginAsync(user, new UserLoginInfo(request.Provider.Name, id.Subject, request.Provider.Settings.DisplayName ?? request.Provider.Name));
                if (add.Succeeded) return user;
                await users.DeleteAsync(user);
                return null;
            }
            db.ChangeTracker.Clear();
            if (!result.Errors.Any(e => e.Code is "DuplicateUserName" or Auth2ErrorCodes.InvalidUserName)) return null;
        }
        return null;
    }

    private string CandidateName(ExternalLoginRequest r, string? email, int attempt)
    {
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        string baseName = attempt >= 3 ? "u" : Sanitize(email?.Split('@')[0] ?? r.Identity.DisplayName ?? "") is { Length: > 1 } n ? n : "user";
        return attempt == 0 && baseName != "u" && baseName != "user" ? baseName : $"{baseName}-{suffix}";
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value.ToLowerInvariant()) if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-') sb.Append(c);
        return sb.Length > 32 ? sb.ToString(0, 32) : sb.ToString();
    }

    private async Task EnsureMembershipAsync(AppUser user, Auth2Application app, CancellationToken ct)
    {
        if (options.Value.Mode == ServerMode.SingleApplication) return;
        if (await db.UserApplications.AnyAsync(x => x.UserId == user.Id && x.ApplicationId == app.Id, ct)) return;
        db.UserApplications.Add(new UserApplication { UserId = user.Id, ApplicationId = app.Id, JoinedAtUtc = clock.GetUtcNow(), Status = UserApplicationStatus.Active, LockoutEnabled = true, SecurityStamp = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateProfileAsync(AppUser user, ExternalIdentity id)
    {
        var changed = false;
        if (id.FirstName is { Length: > 0 } f && f != user.FirstName) { user.FirstName = f; changed = true; }
        if (id.LastName is { Length: > 0 } l && l != user.LastName) { user.LastName = l; changed = true; }
        if (id.DisplayName is { Length: > 0 } d && d != user.DisplayName) { user.DisplayName = d; changed = true; }
        if (id.Phone is { Length: > 0 } ph && ph != user.PhoneNumber) { user.PhoneNumber = ph; changed = true; }
        if (changed) await users.UpdateAsync(user);
    }

    private async Task<ExternalLoginOutcome> RefuseAsync(Auth2Application app, ExternalLoginRequest r, string code, string reason, CancellationToken ct)
    {
        await audit.WriteAsync(Entry(r.IsDirectoryLogin ? "ldap.login.failed" : "external.login.failed", app, null, r,
            new() { ["provider"] = r.Provider.Name, ["code"] = code, ["reason"] = reason }), ct);
        return new ExternalLoginOutcome { ErrorCode = code, ReturnUrl = r.ReturnUrl };
    }

    private static AuditEntry Entry(string action, Auth2Application app, AppUser? user, ExternalLoginRequest r, Dictionary<string, object?> data) =>
        new(action, app.Id, ActorUserId: user?.Id, TargetType: user is null ? null : "user", TargetId: user?.Id.ToString(), IpAddress: r.IpAddress, UserAgent: r.UserAgent, Data: data);
}

/// <inheritdoc />
internal sealed class ExternalMappingService(
    Auth2DbContext db, UserManager<AppUser> users, IAccessService access, IGroupService groups, IAuditService audit, ILogger<ExternalMappingService> log) : IExternalMappingService
{
    /// <inheritdoc />
    public async Task<int> ApplyAsync(Guid applicationId, string providerName, AppUser user, ExternalIdentity identity, CancellationToken ct = default)
    {
        var maps = await db.ExternalGroupMappings.Where(m => m.ApplicationId == applicationId && (m.ProviderName == null || m.ProviderName == providerName)).ToListAsync(ct);
        if (maps.Count == 0) return 0;

        var wanted = new HashSet<(MappingTargetType, Guid)>();
        var mentioned = new HashSet<(MappingTargetType, Guid)>();
        foreach (var m in maps)
        {
            mentioned.Add((m.TargetType, m.TargetId));
            if (Values(m, identity).Any(v => Matches(m, v))) wanted.Add((m.TargetType, m.TargetId));
        }

        // names of the targets (only inside this application; system roles and the GlobalAdmin role can never be given)
        var roles = await db.Roles.Where(r => r.ApplicationId == applicationId && !r.IsSystem && r.Name != Auth2Roles.GlobalAdmin).ToDictionaryAsync(r => r.Id, r => r.Name!, ct);
        var groupNames = await db.Groups.Where(g => g.ApplicationId == applicationId).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var permissionKeys = await db.Permissions.Where(x => x.ApplicationId == applicationId).ToDictionaryAsync(x => x.Id, x => x.Key, ct);

        var currentRoles = (await users.GetRolesAsync(user)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentGroups = (await db.UserGroups.Where(x => x.UserId == user.Id).Select(x => x.GroupId).ToListAsync(ct)).ToHashSet();
        var currentPermissions = (await db.UserPermissions.Where(x => x.UserId == user.Id).Select(x => x.PermissionId).ToListAsync(ct)).ToHashSet();

        var changes = 0;
        foreach (var (type, targetId) in mentioned)
        {
            var want = wanted.Contains((type, targetId));
            switch (type)
            {
                case MappingTargetType.Role when roles.TryGetValue(targetId, out var role):
                {
                    var has = currentRoles.Contains(role);
                    if (want && !has && (await access.AssignRoleAsync(user, role, ct)).Succeeded) changes++;
                    else if (!want && has && (await access.RemoveRoleAsync(user, role, ct)).Succeeded) changes++;
                    break;
                }
                case MappingTargetType.Group when groupNames.TryGetValue(targetId, out var group):
                {
                    var has = currentGroups.Contains(targetId);
                    if (want && !has && (await groups.AddUserAsync(group, user, ct)).Succeeded) changes++;
                    else if (!want && has && (await groups.RemoveUserAsync(group, user, ct)).Succeeded) changes++;
                    break;
                }
                case MappingTargetType.Permission when permissionKeys.TryGetValue(targetId, out var key):
                {
                    var has = currentPermissions.Contains(targetId);
                    if (want && !has && (await access.GrantPermissionAsync(user, key, null, ct)).Succeeded) changes++;
                    else if (!want && has && (await access.RevokePermissionAsync(user, key, ct)).Succeeded) changes++;
                    break;
                }
                default:
                    log.LogWarning("A mapping of provider {Provider} points to a target that is not allowed or does not exist. It was ignored.", providerName);
                    break;
            }
        }
        if (changes > 0)
            await audit.WriteAsync(new AuditEntry("external.mapping.applied", applicationId, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
                Data: new Dictionary<string, object?> { ["provider"] = providerName, ["changes"] = changes }), ct);
        return changes;
    }

    private static IEnumerable<string> Values(ExternalGroupMapping m, ExternalIdentity id) => m.SourceType switch
    {
        MappingSourceType.Group => id.Groups,
        _ => id.Attributes.TryGetValue(m.SourceName, out var v) ? v : [],
    };

    private static bool Matches(ExternalGroupMapping m, string value) => m.MatchType == MappingMatchType.Prefix
        ? value.StartsWith(m.SourceValue, StringComparison.OrdinalIgnoreCase)
        : string.Equals(value, m.SourceValue, StringComparison.OrdinalIgnoreCase);
}

/// <inheritdoc />
internal sealed class ExternalLinkService(UserManager<AppUser> users, Auth2DbContext db, IAuthFlowResolver flow, IAuditService audit, IExternalProviderRegistry registry) : IExternalLinkService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalLinkInfo>> ListAsync(AppUser user, CancellationToken ct = default) =>
        [.. (await users.GetLoginsAsync(user)).Select(l => new ExternalLinkInfo(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName))];

    /// <inheritdoc />
    public async Task<OperationResult> LinkAsync(AppUser user, ExternalIdentity identity, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var owner = await users.FindByLoginAsync(identity.Provider, identity.Subject);
        if (owner is not null) return OperationResult.Fail(owner.Id == user.Id ? Auth2ErrorCodes.Conflict : Auth2ErrorCodes.ExternalClaimsInvalid);
        var display = registry.Get(app.Id, identity.Provider)?.Settings.DisplayName ?? identity.Provider;
        var result = await users.AddLoginAsync(user, new UserLoginInfo(identity.Provider, identity.Subject, display));
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await audit.WriteAsync(new AuditEntry("external.login.linked", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["provider"] = identity.Provider, ["by"] = "self" }), ct);
        return OperationResult.Ok;
    }

    /// <inheritdoc />
    public async Task<OperationResult> UnlinkAsync(AppUser user, string providerName, string providerKey, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        var logins = await users.GetLoginsAsync(user);
        if (!logins.Any(l => l.LoginProvider == providerName && l.ProviderKey == providerKey)) return OperationResult.Fail(Auth2ErrorCodes.NotFound);

        // A directory login cannot be removed while the directory is the only way to check passwords.
        if (registry.Get(app.Id, providerName) is { Settings: LdapProviderSettings { Mode: LdapMode.LdapOnly } })
            return OperationResult.Fail(Auth2ErrorCodes.CannotRemoveLastLoginMethod);

        var others = logins.Count - 1;
        if (await users.HasPasswordAsync(user)) others++;
        var methods = settings.Methods.AllowedLoginMethods;
        if (user.EmailConfirmed && !string.IsNullOrEmpty(user.Email) && methods.Contains(LoginMethod.EmailOtp)) others++;
        if (user.PhoneNumberConfirmed && !string.IsNullOrEmpty(user.PhoneNumber) && methods.Contains(LoginMethod.PhoneOtp)) others++;
        var passkeys = await db.Set<AppUserPasskey>().CountAsync(x => x.UserId == user.Id, ct);
        if (others + passkeys < 1) return OperationResult.Fail(Auth2ErrorCodes.CannotRemoveLastLoginMethod);
        // Backup rule: a person who would depend on one passkey alone must first register a second one (a lost device would lock them out).
        if (settings.Passkeys.RequirePasskeyBackupMethod && others == 0 && passkeys == 1) return OperationResult.Fail(Auth2ErrorCodes.CannotRemoveLastLoginMethod);

        var result = await users.RemoveLoginAsync(user, providerName, providerKey);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await audit.WriteAsync(new AuditEntry("external.login.unlinked", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["provider"] = providerName }), ct);
        return OperationResult.Ok;
    }
}
