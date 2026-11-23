using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// One-time codes in the <c>otp_codes</c> table. Rules (from the application settings): length, lifetime, max wrong attempts, resend delay.
/// A code is hashed, works once, and is compared in constant time. "Works once" is enforced by one conditional UPDATE, so two
/// parallel requests cannot both use the same code.
/// </summary>
public sealed class OtpService(Auth2DbContext db, IAuthFlowResolver flow, ISecretHasher hasher, TimeProvider clock) : IOtpService
{
    /// <inheritdoc />
    public async Task<OtpIssueResult> IssueAsync(AppUser user, string purpose, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var rules = (await flow.GetSettingsAsync(ct)).TwoFactor;
        var now = clock.GetUtcNow();

        var open = await db.OtpCodes.Where(c => c.UserId == user.Id && c.ApplicationId == app.Id && c.Purpose == purpose && c.ConsumedAtUtc == null).ToListAsync(ct);
        // The creation time is the expiry minus the lifetime that was in force then; the newest code decides the resend delay.
        if (open.Count > 0 && now < open.Max(c => c.ExpiresAtUtc) - TimeSpan.FromSeconds(rules.OtpLifetimeSeconds) + TimeSpan.FromSeconds(rules.OtpResendSeconds))
            return new OtpIssueResult(null, TooSoon: true);

        foreach (var old in open) old.ConsumedAtUtc = now;   // only one open code per user and purpose

        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, rules.OtpLength)).ToString("D" + rules.OtpLength, System.Globalization.CultureInfo.InvariantCulture);
        db.OtpCodes.Add(new OtpCode
        {
            UserId = user.Id,
            ApplicationId = app.Id,
            Purpose = purpose,
            CodeHash = hasher.Hash(code),
            ExpiresAtUtc = now.AddSeconds(rules.OtpLifetimeSeconds),
        });
        await db.SaveChangesAsync(ct);
        return new OtpIssueResult(code, TooSoon: false);
    }

    /// <inheritdoc />
    public async Task<OtpVerifyResult> VerifyAsync(AppUser user, string purpose, string code, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var rules = (await flow.GetSettingsAsync(ct)).TwoFactor;
        var now = clock.GetUtcNow();

        // No tracking: the updates below bypass the change tracker, so a tracked copy would show old attempt counts.
        var row = await db.OtpCodes.AsNoTracking()
            .Where(c => c.UserId == user.Id && c.ApplicationId == app.Id && c.Purpose == purpose && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.ExpiresAtUtc).FirstOrDefaultAsync(ct);
        if (row is null) return OtpVerifyResult.NotFound;

        if (now > row.ExpiresAtUtc)
        {
            await CloseAsync(row.Id, now, ct);
            return OtpVerifyResult.Expired;
        }
        if (row.Attempts >= rules.OtpMaxAttempts)
        {
            await CloseAsync(row.Id, now, ct);
            return OtpVerifyResult.TooManyAttempts;
        }

        if (!hasher.Verify(code, row.CodeHash))
        {
            var attempts = row.Attempts + 1;
            await db.OtpCodes.Where(c => c.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, attempts), ct);
            if (attempts >= rules.OtpMaxAttempts)
            {
                await CloseAsync(row.Id, now, ct);
                return OtpVerifyResult.TooManyAttempts;
            }
            return OtpVerifyResult.Invalid;
        }

        // Use it up. Only one caller can win this update.
        var used = await db.OtpCodes.Where(c => c.Id == row.Id && c.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, now), ct);
        return used == 1 ? OtpVerifyResult.Ok : OtpVerifyResult.Invalid;
    }

    private Task<int> CloseAsync(Guid id, DateTimeOffset now, CancellationToken ct) =>
        db.OtpCodes.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, now), ct);
}
