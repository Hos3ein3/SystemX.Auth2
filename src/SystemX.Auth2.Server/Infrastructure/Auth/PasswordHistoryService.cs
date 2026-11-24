using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>See <see cref="IPasswordHistoryService"/>.</summary>
internal sealed class PasswordHistoryService(
    Auth2DbContext db, IAuthFlowResolver flow, ICredentialStateProvider credentials, IPasswordHasher<AppUser> hasher, TimeProvider clock) : IPasswordHistoryService
{
    public async Task<bool> IsReusedAsync(AppUser user, string newPassword, CancellationToken ct = default)
    {
        var rules = (await flow.GetSettingsAsync(ct)).Password;
        if (!rules.SaveOldPasswords) return false;

        // The current password is never allowed again.
        var current = await (await credentials.GetAsync(ct)).GetPasswordHashAsync(user, ct);
        if (current is not null && Matches(user, current, newPassword)) return true;

        // Old ones: all remembered ones, or only the one used right before the current password.
        var take = rules.AllowOldPasswordReuse ? 1 : Math.Clamp(rules.OldPasswordsToKeep, 1, 24);
        var scope = await ScopeAsync(ct);
        var old = await db.PasswordHistory.AsNoTracking().Where(h => h.UserId == user.Id && h.CredentialProjectId == scope)
            .OrderByDescending(h => h.SavedAtUtc).ThenByDescending(h => h.Id).Take(take).Select(h => h.PasswordHash).ToListAsync(ct);
        return old.Any(hash => Matches(user, hash, newPassword));
    }

    public async Task RememberAsync(AppUser user, string oldPasswordHash, CancellationToken ct = default)
    {
        var rules = (await flow.GetSettingsAsync(ct)).Password;
        if (!rules.SaveOldPasswords || string.IsNullOrEmpty(oldPasswordHash)) return;

        var scope = await ScopeAsync(ct);
        var keep = Math.Clamp(rules.OldPasswordsToKeep, 1, 24);
        // Room for the new row: keep the newest (keep - 1) of the existing ones.
        var existing = await db.PasswordHistory.Where(h => h.UserId == user.Id && h.CredentialProjectId == scope)
            .OrderByDescending(h => h.SavedAtUtc).ThenByDescending(h => h.Id).ToListAsync(ct);
        db.PasswordHistory.RemoveRange(existing.Skip(keep - 1));
        db.PasswordHistory.Add(new PasswordHistoryEntry { UserId = user.Id, CredentialProjectId = scope, PasswordHash = oldPasswordHash, SavedAtUtc = clock.GetUtcNow() });
    }

    public async Task<bool> IsExpiredAsync(AppUser user, CancellationToken ct = default)
    {
        var days = (await flow.GetSettingsAsync(ct)).Password.PasswordExpiryDays;
        if (days <= 0) return false;
        var changed = await (await credentials.GetAsync(ct)).GetPasswordChangedAtAsync(user, ct);
        return changed is { } at && clock.GetUtcNow() - at > TimeSpan.FromDays(days);
    }

    /// <summary>The project when credentials are per project, null when they are shared.</summary>
    private async Task<Guid?> ScopeAsync(CancellationToken ct) =>
        await flow.GetCredentialScopeAsync(ct) == CredentialScope.PerProject ? (await flow.GetProjectAsync(ct)).Id : null;

    private bool Matches(AppUser user, string hash, string password) =>
        hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;
}
