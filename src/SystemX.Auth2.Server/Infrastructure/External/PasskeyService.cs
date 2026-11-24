using System.Buffers.Text;
using System.Formats.Cbor;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <inheritdoc />
internal sealed class PasskeyService(
    SignInManager<AppUser> signIn, UserManager<AppUser> users, Auth2DbContext db, IAuthFlowResolver flow, ILoginService login, UserLookup lookup,
    IAuditService audit, IServerValues serverValues, IOptions<Auth2ServerOptions> options, IHostEnvironment env) : IPasskeyService
{
    private async Task<(Project App, PasskeySettings Settings, ProjectSettings All)> GateAsync(CancellationToken ct)
    {
        var app = await flow.GetProjectAsync(ct);
        var all = await flow.GetSettingsAsync(ct);
        return (app, all.Passkeys, all);
    }

    /// <summary>Passkeys need an explicit server domain in Production. Without it the Server refuses to use them.</summary>
    private bool DomainIsOk => !env.IsProduction() || !string.IsNullOrWhiteSpace(options.Value.Passkeys.ServerDomain);

    private async Task<string?> RefusalAsync(CancellationToken ct, bool forSignIn = false)
    {
        var (app, passkeys, all) = await GateAsync(ct);
        if (!app.IsEnabled) return Auth2ErrorCodes.ProjectDisabled;
        if (!passkeys.AllowPasskeys || !DomainIsOk) return Auth2ErrorCodes.PasskeyNotAllowed;
        if (forSignIn && !all.Methods.AllowedLoginMethods.Contains(LoginMethod.Passkey)) return Auth2ErrorCodes.LoginMethodNotAllowed;
        return null;
    }

    // ------------------------------------------------------------------------------------ registration

    public async Task<PasskeyOutcome<string>> CreationOptionsAsync(AppUser user, CancellationToken ct = default)
    {
        if (await RefusalAsync(ct) is { } refused) return PasskeyOutcome<string>.Fail(refused);
        var (_, passkeys, _) = await GateAsync(ct);
        if ((await users.GetPasskeysAsync(user)).Count >= passkeys.MaxPasskeysPerUser) return PasskeyOutcome<string>.Fail(Auth2ErrorCodes.PasskeyLimitReached);
        var json = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = user.Id.ToString("D"), Name = user.UserName ?? user.Email ?? user.Id.ToString("D"), DisplayName = user.DisplayName ?? user.UserName ?? "" });
        return PasskeyOutcome<string>.Ok(Adjust(json, passkeys, creation: true));
    }

    public async Task<PasskeyOutcome<PasskeyDto>> RegisterAsync(AppUser user, string credentialJson, string? name, CancellationToken ct = default)
    {
        if (await RefusalAsync(ct) is { } refused) return PasskeyOutcome<PasskeyDto>.Fail(refused);
        var (app, passkeys, _) = await GateAsync(ct);
        var existing = await users.GetPasskeysAsync(user);
        if (existing.Count >= passkeys.MaxPasskeysPerUser) return PasskeyOutcome<PasskeyDto>.Fail(Auth2ErrorCodes.PasskeyLimitReached);
        var cleanName = (name ?? "").Trim();
        if (cleanName.Length > passkeys.PasskeyNameMaxLength) return PasskeyOutcome<PasskeyDto>.Fail(Auth2ErrorCodes.ValidationFailed);

        PasskeyAttestationResult attestation;
        try { attestation = await signIn.PerformPasskeyAttestationAsync(credentialJson); }
        catch (Exception ex) when (ex is JsonException or PasskeyException or FormatException or InvalidOperationException)
        {
            return PasskeyOutcome<PasskeyDto>.Fail(Auth2ErrorCodes.ValidationFailed);
        }
        if (!attestation.Succeeded) return PasskeyOutcome<PasskeyDto>.Fail(Auth2ErrorCodes.ValidationFailed);
        var passkey = attestation.Passkey;
        if (passkeys.PasskeyUserVerification == "required" && !passkey.IsUserVerified) return PasskeyOutcome<PasskeyDto>.Fail(Auth2ErrorCodes.ValidationFailed);

        var aaguid = AaguidOf(passkey.AttestationObject);
        var server = options.Value.Passkeys;
        var blocked = server.BlockedAaguids.AsEnumerable();
        if (await serverValues.GetAsync("passkeys.blocked_aaguids", ct) is { Length: > 0 } stored) blocked = blocked.Concat(stored.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse));
        if (aaguid is { } id && blocked.Contains(id)) return await BlockedAsync(app, user, id);
        if (server.AttestationMode == "AaguidAllowList" && (aaguid is null || !server.AllowedAaguids.Contains(aaguid.Value))) return await BlockedAsync(app, user, aaguid);

        // The same credential id must not exist for any user in this application.
        if (await users.FindByPasskeyIdAsync(passkey.CredentialId) is not null) return PasskeyOutcome<PasskeyDto>.Fail(Auth2ErrorCodes.Conflict);

        passkey.Name = cleanName.Length > 0 ? cleanName : null;
        var saved = await users.AddOrUpdatePasskeyAsync(user, passkey);
        if (!saved.Succeeded) return PasskeyOutcome<PasskeyDto>.Fail(Auth2ErrorCodes.ValidationFailed);
        if (aaguid is not null)
        {
            var row = await db.Set<AppUserPasskey>().FirstAsync(x => x.ProjectId == app.Id && x.CredentialId == passkey.CredentialId, ct);
            row.Aaguid = aaguid;
            await db.SaveChangesAsync(ct);
        }
        await audit.WriteAsync(new AuditEntry("passkey.registered", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["backupEligible"] = passkey.IsBackupEligible }), ct);
        return PasskeyOutcome<PasskeyDto>.Ok(ToDto(passkey));
    }

    private async Task<PasskeyOutcome<PasskeyDto>> BlockedAsync(Project app, AppUser user, Guid? aaguid)
    {
        await audit.WriteAsync(new AuditEntry("passkey.blocked", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
            Data: new Dictionary<string, object?> { ["aaguid"] = aaguid?.ToString() }));
        return PasskeyOutcome<PasskeyDto>.Fail(Auth2ErrorCodes.PasskeyBlockedAuthenticator);
    }

    /// <summary>The authenticator model id inside the attestation object (<c>authData</c> bytes 37 to 52), or null.</summary>
    internal static Guid? AaguidOf(byte[]? attestationObject)
    {
        if (attestationObject is null) return null;
        try
        {
            var reader = new CborReader(attestationObject, CborConformanceMode.Lax);
            var entries = reader.ReadStartMap();
            for (var i = 0; i < entries; i++)
            {
                var key = reader.ReadTextString();
                if (key != "authData") { reader.SkipValue(); continue; }
                var data = reader.ReadByteString();
                if (data.Length < 55 || (data[32] & 0x40) == 0) return null;   // no attested credential data
                var guid = new Guid(data.AsSpan(37, 16), bigEndian: true);
                return guid == Guid.Empty ? null : guid;
            }
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException) { }
        return null;
    }

    // ------------------------------------------------------------------------------------ sign-in

    public async Task<PasskeyOutcome<string>> RequestOptionsAsync(string? login, CancellationToken ct = default)
    {
        if (await RefusalAsync(ct, forSignIn: true) is { } refused) return PasskeyOutcome<string>.Fail(refused);
        var (_, passkeys, all) = await GateAsync(ct);
        AppUser? user = null;
        if (!string.IsNullOrWhiteSpace(login))
        {
            // An unknown name gives options like any other: nobody can find out who exists.
            user = await lookup.FindAsync(login, all, ct);
        }
        var json = await signIn.MakePasskeyRequestOptionsAsync(user!);
        return PasskeyOutcome<string>.Ok(Adjust(json, passkeys, creation: false));
    }

    public async Task<LoginResult> SignInAsync(string credentialJson, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var (app, passkeys, _) = await GateAsync(ct);
        if (await RefusalAsync(ct, forSignIn: true) is { } refused) return LoginResult.Fail(refused);

        PasskeyAssertionResult<AppUser> assertion;
        try { assertion = await signIn.PerformPasskeyAssertionAsync(credentialJson); }
        catch (Exception ex) when (ex is JsonException or PasskeyException or FormatException or InvalidOperationException)
        {
            await audit.WriteAsync(new AuditEntry("passkey.login.failed", app.Id, IpAddress: ipAddress, UserAgent: userAgent, Data: new Dictionary<string, object?> { ["reason"] = "malformed" }), ct);
            return LoginResult.Fail(Auth2ErrorCodes.InvalidCredentials);
        }
        // A passkey of another application is "not found" here, because the store only sees this application.
        if (!assertion.Succeeded || assertion.User is null)
        {
            await audit.WriteAsync(new AuditEntry("passkey.login.failed", app.Id, IpAddress: ipAddress, UserAgent: userAgent, Data: new Dictionary<string, object?> { ["reason"] = "assertion_failed" }), ct);
            return LoginResult.Fail(Auth2ErrorCodes.InvalidCredentials);
        }
        var user = assertion.User;
        var passkey = assertion.Passkey;
        var verified = passkey.IsUserVerified;
        if (passkeys.PasskeyUserVerification == "required" && !verified)
        {
            await audit.WriteAsync(new AuditEntry("passkey.login.failed", app.Id, ActorUserId: user.Id, IpAddress: ipAddress, Data: new Dictionary<string, object?> { ["reason"] = "not_user_verified" }), ct);
            return LoginResult.Fail(Auth2ErrorCodes.InvalidCredentials);
        }
        await users.AddOrUpdatePasskeyAsync(user, passkey);   // the new signature counter

        // A passkey with user verification is something the user has and something they are or know: it counts as several factors.
        string[] amr = verified ? [passkey.IsBackupEligible ? "swk" : "hwk", "mfa"] : [passkey.IsBackupEligible ? "swk" : "hwk"];
        return await login.ExternalLoginAsync(new ExternalSignInRequest(user, LoginMethod.Passkey, null, amr, SkipLocalTwoFactor: verified, ipAddress, userAgent), ct);
    }

    // ------------------------------------------------------------------------------------ management

    public async Task<IReadOnlyList<PasskeyDto>> ListAsync(AppUser user, CancellationToken ct = default) =>
        [.. (await users.GetPasskeysAsync(user)).OrderBy(p => p.CreatedAt).Select(ToDto)];

    public async Task<OperationResult> RenameAsync(AppUser user, string id, string name, CancellationToken ct = default)
    {
        var (_, passkeys, _) = await GateAsync(ct);
        name = name.Trim();
        if (name.Length == 0 || name.Length > passkeys.PasskeyNameMaxLength) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        if (!TryId(id, out var bytes)) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var passkey = await users.GetPasskeyAsync(user, bytes);
        if (passkey is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        passkey.Name = name;
        var result = await users.AddOrUpdatePasskeyAsync(user, passkey);
        return result.Succeeded ? OperationResult.Ok : OperationResult.Fail(result.Errors);
    }

    public async Task<OperationResult> RemoveAsync(AppUser user, string id, CancellationToken ct = default)
    {
        var (app, _, all) = await GateAsync(ct);
        if (!TryId(id, out var bytes)) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var current = await users.GetPasskeysAsync(user);
        if (current.All(p => !p.CredentialId.AsSpan().SequenceEqual(bytes))) return OperationResult.Fail(Auth2ErrorCodes.NotFound);

        // The user must keep at least one way to sign in.
        var remaining = current.Count - 1 + (await users.GetLoginsAsync(user)).Count + (await users.HasPasswordAsync(user) ? 1 : 0);
        var m = all.Methods.AllowedLoginMethods;
        if (user.EmailConfirmed && !string.IsNullOrEmpty(user.Email) && m.Contains(LoginMethod.EmailOtp)) remaining++;
        if (user.PhoneNumberConfirmed && !string.IsNullOrEmpty(user.PhoneNumber) && m.Contains(LoginMethod.PhoneOtp)) remaining++;
        if (remaining < 1) return OperationResult.Fail(Auth2ErrorCodes.CannotRemoveLastLoginMethod);

        var result = await users.RemovePasskeyAsync(user, bytes);
        if (!result.Succeeded) return OperationResult.Fail(result.Errors);
        await audit.WriteAsync(new AuditEntry("passkey.removed", app.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString()), ct);
        return OperationResult.Ok;
    }

    // ------------------------------------------------------------------------------------ helpers

    private static bool TryId(string id, out byte[] bytes)
    {
        try { bytes = Base64Url.DecodeFromChars(id); return bytes.Length > 0; }
        catch (FormatException) { bytes = []; return false; }
    }

    internal static PasskeyDto ToDto(UserPasskeyInfo p) =>
        new(Base64Url.EncodeToString(p.CredentialId), p.Name, p.CreatedAt, p.SignCount, p.IsBackedUp, p.IsBackupEligible, p.IsUserVerified, p.Transports ?? []);

    /// <summary>
    /// The global Identity options are the same for all applications, so the per-application user verification and resident key rules
    /// are written into the options JSON here. The rules are enforced again on the server after the answer (see <see cref="RegisterAsync"/> and <see cref="SignInAsync"/>).
    /// </summary>
    private static string Adjust(string json, PasskeySettings s, bool creation)
    {
        var root = JsonNode.Parse(json)!;
        var node = root["publicKey"] ?? root;
        if (creation)
        {
            var selection = node["authenticatorSelection"] as JsonObject ?? new JsonObject();
            selection["userVerification"] = s.PasskeyUserVerification;
            selection["residentKey"] = s.PasskeyResidentKey;
            selection["requireResidentKey"] = s.PasskeyResidentKey == "required";
            node["authenticatorSelection"] = selection;
        }
        else node["userVerification"] = s.PasskeyUserVerification;
        return root.ToJsonString();
    }
}
