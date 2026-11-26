using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Persistence.Identity;

/// <summary>Shared helpers for both strategies.</summary>
public abstract class CredentialStateBase(IDataProtectionProvider protection, ISecretHasher hasher)
{
    private readonly IDataProtector _protector = protection.CreateProtector("SystemX.Auth2.AuthenticatorKey");

    /// <summary>The secret hasher.</summary>
    protected ISecretHasher Hasher { get; } = hasher;

    /// <summary>Encrypts a TOTP key.</summary>
    protected string? Protect(string? key) => key is null ? null : _protector.Protect(key);

    /// <summary>Decrypts a TOTP key.</summary>
    protected string? Unprotect(string? value) => value is null ? null : _protector.Unprotect(value);

    /// <summary>Generates normalization variations for a recovery code (trimmed, uppercase, with/without hyphens).</summary>
    protected static IEnumerable<string> CandidateCodes(string code)
    {
        var trimmed = (code ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) yield break;

        yield return trimmed;

        var upper = trimmed.ToUpperInvariant();
        if (upper != trimmed) yield return upper;

        var clean = trimmed.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (clean != upper && clean != trimmed) yield return clean;

        if (clean.Length == 10)
        {
            var withHyphen = $"{clean[..5]}-{clean[5..]}";
            if (withHyphen != trimmed && withHyphen != upper)
                yield return withHyphen;
        }
    }
}

/// <summary>Credentials in the user row (and the user token table). Used for <see cref="CredentialScope.Shared"/> and single mode.</summary>
public sealed class UserRowCredentialState(Auth2DbContext db, IDataProtectionProvider protection, ISecretHasher hasher)
    : CredentialStateBase(protection, hasher), ICredentialState
{
    private const string TokenProvider = "[Auth2]";
    private const string KeyToken = "AuthenticatorKey";
    private const string CodesToken = "RecoveryCodeHashes";

    /// <inheritdoc />
    public Task<string?> GetPasswordHashAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.PasswordHash);
    /// <inheritdoc />
    public Task SetPasswordHashAsync(AppUser u, string? hash, CancellationToken ct) { u.PasswordHash = hash; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task<string?> GetSecurityStampAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.SecurityStamp);
    /// <inheritdoc />
    public Task SetSecurityStampAsync(AppUser u, string stamp, CancellationToken ct) { u.SecurityStamp = stamp; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task<bool> GetTwoFactorEnabledAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.TwoFactorEnabled);
    /// <inheritdoc />
    public Task SetTwoFactorEnabledAsync(AppUser u, bool enabled, CancellationToken ct) { u.TwoFactorEnabled = enabled; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLockoutEndAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.LockoutEnd);
    /// <inheritdoc />
    public Task SetLockoutEndAsync(AppUser u, DateTimeOffset? end, CancellationToken ct) { u.LockoutEnd = end; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task<int> GetAccessFailedCountAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.AccessFailedCount);
    /// <inheritdoc />
    public Task<int> IncrementAccessFailedCountAsync(AppUser u, CancellationToken ct) => Task.FromResult(++u.AccessFailedCount);
    /// <inheritdoc />
    public Task ResetAccessFailedCountAsync(AppUser u, CancellationToken ct) { u.AccessFailedCount = 0; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task<bool> GetLockoutEnabledAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.LockoutEnabled);
    /// <inheritdoc />
    public Task SetLockoutEnabledAsync(AppUser u, bool enabled, CancellationToken ct) { u.LockoutEnabled = enabled; return Task.CompletedTask; }
    /// <inheritdoc />
    public void BeforeUserCreated(AppUser user) { }

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetPasswordChangedAtAsync(AppUser u, CancellationToken ct) =>
        Task.FromResult<DateTimeOffset?>(u.PasswordHash is null ? null : u.PasswordChangedAtUtc ?? u.CreatedAtUtc);
    /// <inheritdoc />
    public Task SetPasswordChangedAtAsync(AppUser u, DateTimeOffset? changedAt, CancellationToken ct) { u.PasswordChangedAtUtc = changedAt; return Task.CompletedTask; }

    /// <inheritdoc />
    public async Task<string?> GetAuthenticatorKeyAsync(AppUser u, CancellationToken ct) =>
        Unprotect((await FindTokenAsync(u, KeyToken, ct))?.Value);

    /// <inheritdoc />
    public Task SetAuthenticatorKeyAsync(AppUser u, string? key, CancellationToken ct) => SetTokenAsync(u, KeyToken, Protect(key), ct);

    /// <inheritdoc />
    public async Task ReplaceRecoveryCodesAsync(AppUser u, IEnumerable<string> codes, CancellationToken ct) =>
        await SetTokenAsync(u, CodesToken, string.Join(';', codes.Select(Hasher.Hash)), ct);

    /// <inheritdoc />
    public async Task<bool> RedeemRecoveryCodeAsync(AppUser u, string code, CancellationToken ct)
    {
        var token = await FindTokenAsync(u, CodesToken, ct);
        if (string.IsNullOrEmpty(token?.Value)) return false;
        var hashes = token.Value.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var candidates = CandidateCodes(code).ToList();   // once: every check of a hash is slow on purpose
        var match = hashes.FindIndex(h => candidates.Any(c => Hasher.Verify(c, h)));
        if (match < 0) return false;
        hashes.RemoveAt(match);
        token.Value = string.Join(';', hashes);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> CountRecoveryCodesAsync(AppUser u, CancellationToken ct) =>
        (await FindTokenAsync(u, CodesToken, ct))?.Value?.Split(';', StringSplitOptions.RemoveEmptyEntries).Length ?? 0;

    private async Task<AppUserToken?> FindTokenAsync(AppUser u, string name, CancellationToken ct) =>
        await db.UserTokens.FindAsync([u.Id, TokenProvider, name], ct);

    private async Task SetTokenAsync(AppUser u, string name, string? value, CancellationToken ct)
    {
        var token = await FindTokenAsync(u, name, ct);
        if (value is null)
        {
            if (token is not null) db.UserTokens.Remove(token);
        }
        else if (token is null) db.UserTokens.Add(new AppUserToken { UserId = u.Id, LoginProvider = TokenProvider, Name = name, Value = value });
        else token.Value = value;
    }
}

/// <summary>
/// Credentials in the <c>user_applications</c> row of the current application.
/// A person with no row for this application has no credentials here, so a login with a password from another application fails.
/// A password can only be set when the row exists, or when the user is new (the row is saved with the user in one transaction).
/// </summary>
public sealed class UserProjectCredentialState(
    Auth2DbContext db,
    IProjectContext applications,
    IDataProtectionProvider protection,
    ISecretHasher hasher,
    TimeProvider clock) : CredentialStateBase(protection, hasher), ICredentialState
{
    private readonly Dictionary<Guid, UserProject> _pending = [];

    /// <inheritdoc />
    public async Task<string?> GetPasswordHashAsync(AppUser u, CancellationToken ct) => (await RowAsync(u, false, ct))?.PasswordHash;
    /// <inheritdoc />
    public async Task SetPasswordHashAsync(AppUser u, string? hash, CancellationToken ct) => (await RowAsync(u, true, ct))!.PasswordHash = hash;
    /// <inheritdoc />
    public async Task<string?> GetSecurityStampAsync(AppUser u, CancellationToken ct)
    {
        var row = await RowAsync(u, false, ct);
        if (row is null) return null;
        // A member without credentials yet (for example added by an admin) still needs a stamp, or Identity refuses to save the user.
        return row.SecurityStamp ??= Guid.NewGuid().ToString("N");
    }
    /// <inheritdoc />
    public async Task SetSecurityStampAsync(AppUser u, string stamp, CancellationToken ct) => (await RowAsync(u, true, ct))!.SecurityStamp = stamp;
    /// <inheritdoc />
    public async Task<bool> GetTwoFactorEnabledAsync(AppUser u, CancellationToken ct) => (await RowAsync(u, false, ct))?.TwoFactorEnabled ?? false;
    /// <inheritdoc />
    public async Task SetTwoFactorEnabledAsync(AppUser u, bool enabled, CancellationToken ct) => (await RowAsync(u, true, ct))!.TwoFactorEnabled = enabled;
    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLockoutEndAsync(AppUser u, CancellationToken ct) => (await RowAsync(u, false, ct))?.LockoutEnd;
    /// <inheritdoc />
    public async Task SetLockoutEndAsync(AppUser u, DateTimeOffset? end, CancellationToken ct) => (await RowAsync(u, true, ct))!.LockoutEnd = end;
    /// <inheritdoc />
    public async Task<int> GetAccessFailedCountAsync(AppUser u, CancellationToken ct) => (await RowAsync(u, false, ct))?.AccessFailedCount ?? 0;
    /// <inheritdoc />
    public async Task<int> IncrementAccessFailedCountAsync(AppUser u, CancellationToken ct) => ++(await RowAsync(u, true, ct))!.AccessFailedCount;
    /// <inheritdoc />
    public async Task ResetAccessFailedCountAsync(AppUser u, CancellationToken ct) => (await RowAsync(u, true, ct))!.AccessFailedCount = 0;
    /// <inheritdoc />
    public async Task<bool> GetLockoutEnabledAsync(AppUser u, CancellationToken ct) => (await RowAsync(u, false, ct))?.LockoutEnabled ?? false;
    /// <inheritdoc />
    public async Task SetLockoutEnabledAsync(AppUser u, bool enabled, CancellationToken ct) => (await RowAsync(u, true, ct))!.LockoutEnabled = enabled;
    /// <inheritdoc />
    public async Task<string?> GetAuthenticatorKeyAsync(AppUser u, CancellationToken ct) => Unprotect((await RowAsync(u, false, ct))?.AuthenticatorKey);
    /// <inheritdoc />
    public async Task SetAuthenticatorKeyAsync(AppUser u, string? key, CancellationToken ct) => (await RowAsync(u, true, ct))!.AuthenticatorKey = Protect(key);

    /// <inheritdoc />
    public void BeforeUserCreated(AppUser user)
    {
        if (_pending.Remove(user.Id, out var row)) db.UserProjects.Add(row);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetPasswordChangedAtAsync(AppUser u, CancellationToken ct)
    {
        var row = await RowAsync(u, false, ct);
        return row?.PasswordHash is null ? null : row.PasswordChangedAtUtc ?? row.JoinedAtUtc;
    }
    /// <inheritdoc />
    public async Task SetPasswordChangedAtAsync(AppUser u, DateTimeOffset? changedAt, CancellationToken ct) => (await RowAsync(u, true, ct))!.PasswordChangedAtUtc = changedAt;

    /// <inheritdoc />
    public async Task ReplaceRecoveryCodesAsync(AppUser u, IEnumerable<string> codes, CancellationToken ct)
    {
        var app = await applications.GetRequiredAsync(ct);
        var old = await db.RecoveryCodes.Where(c => c.UserId == u.Id && c.ProjectId == app.Id).ToListAsync(ct);
        db.RecoveryCodes.RemoveRange(old);
        foreach (var code in codes)
            db.RecoveryCodes.Add(new UserProjectRecoveryCode { UserId = u.Id, ProjectId = app.Id, CodeHash = Hasher.Hash(code) });
    }

    /// <inheritdoc />
    public async Task<bool> RedeemRecoveryCodeAsync(AppUser u, string code, CancellationToken ct)
    {
        var app = await applications.GetRequiredAsync(ct);
        var rows = await db.RecoveryCodes.Where(c => c.UserId == u.Id && c.ProjectId == app.Id && c.UsedAtUtc == null).ToListAsync(ct);
        var candidates = CandidateCodes(code).ToList();   // once: every check of a hash is slow on purpose
        var match = rows.FirstOrDefault(r => candidates.Any(c => Hasher.Verify(c, r.CodeHash)));
        if (match is null) return false;
        match.UsedAtUtc = clock.GetUtcNow();
        return true;
    }

    /// <inheritdoc />
    public async Task<int> CountRecoveryCodesAsync(AppUser u, CancellationToken ct)
    {
        var app = await applications.GetRequiredAsync(ct);
        return await db.RecoveryCodes.CountAsync(c => c.UserId == u.Id && c.ProjectId == app.Id && c.UsedAtUtc == null, ct);
    }

    private async Task<UserProject?> RowAsync(AppUser u, bool forWrite, CancellationToken ct)
    {
        if (_pending.TryGetValue(u.Id, out var pending)) return pending;
        var app = await applications.GetRequiredAsync(ct);
        var row = await db.UserProjects.FindAsync([u.Id, app.Id], ct);
        if (row is not null || !forWrite) return row;

        // A new user (not saved yet) gets its row saved together with the user.
        if (db.Entry(u).State == EntityState.Detached)
        {
            row = new UserProject { UserId = u.Id, ProjectId = app.Id, JoinedAtUtc = clock.GetUtcNow(), Status = UserProjectStatus.Active };
            _pending[u.Id] = row;
            return row;
        }
        // An existing person must join the application on purpose (with proof of ownership) before credentials exist.
        throw new InvalidOperationException($"{Auth2ErrorCodes.UserNotInProject}: the user has no row for this application.");
    }
}

/// <summary>Picks the strategy by the effective credential scope.</summary>
public sealed class CredentialStateProvider(IAuthFlowResolver flow, UserRowCredentialState userRow, UserProjectCredentialState perProject) : ICredentialStateProvider
{
    /// <inheritdoc />
    public async Task<ICredentialState> GetAsync(CancellationToken ct = default) =>
        await flow.GetCredentialScopeAsync(ct) == CredentialScope.PerProject ? perProject : userRow;
}
