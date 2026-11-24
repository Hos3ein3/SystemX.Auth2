using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>
/// Keys in the database. One singleton per instance holds the current keys in memory and tells OpenIddict when they change
/// (through an options change token), so a rotation done by any instance is picked up by all of them without a restart.
/// </summary>
public sealed class KeyRing(IServiceScopeFactory scopes, IDataProtectionProvider protection, IOptions<Auth2KeyOptions> options, TimeProvider clock, ILogger<KeyRing> log)
    : IKeyRing, IOptionsChangeTokenSource<OpenIddictServerOptions>
{
    private sealed record Snapshot(string Fingerprint, IReadOnlyList<(KeyInfo Info, X509Certificate2 Cert)> Keys);

    /// <summary>Keys are kept in memory only. macOS cannot do that, so it uses the default key set there (development machines).</summary>
    internal static readonly X509KeyStorageFlags KeyStorageFlags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;

    private static readonly Snapshot Empty = new("", []);
    private readonly object _gate = new();
    private Snapshot _snapshot = Empty;
    private CancellationTokenSource _changed = new();

    private IDataProtector Protector => protection.CreateProtector("SystemX.Auth2.KeyRing");

    /// <inheritdoc />
    public string? Name => Options.DefaultName;

    /// <inheritdoc />
    public Microsoft.Extensions.Primitives.IChangeToken GetChangeToken() => new Microsoft.Extensions.Primitives.CancellationChangeToken(_changed.Token);

    /// <summary>Credentials for OpenIddict, newest key first. Loads (and creates) the keys on first use.</summary>
    internal (List<SigningCredentials> Signing, List<EncryptingCredentials> Encryption) Credentials()
    {
        try
        {
            if (_snapshot.Keys.Count == 0) RefreshAsync().GetAwaiter().GetResult();
            if (_snapshot.Keys.Count == 0) EnsureAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UndefinedTable } or Npgsql.NpgsqlException)
        {
            // The database is not ready (not migrated, or down). Temporary keys keep the server answering; the refresh service
            // reads the real keys as soon as they exist and the options are built again.
            log.LogWarning("The key ring is not readable yet ({Reason}). Temporary keys are used until the database is ready.", ex.GetType().Name);
            return Temporary();
        }
        var keys = _snapshot.Keys.OrderByDescending(k => k.Info.NotAfterUtc).ToList();
        return (
            [.. keys.Where(k => k.Info.Purpose == nameof(KeyPurpose.Signing)).Select(k => new SigningCredentials(new X509SecurityKey(k.Cert), SecurityAlgorithms.RsaSha256))],
            [.. keys.Where(k => k.Info.Purpose == nameof(KeyPurpose.Encryption)).Select(k => new EncryptingCredentials(new X509SecurityKey(k.Cert), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512))]);
    }

    private static (List<SigningCredentials>, List<EncryptingCredentials>) Temporary()
    {
        var signing = new RsaSecurityKey(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };
        var encryption = new RsaSecurityKey(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };
        return ([new SigningCredentials(signing, SecurityAlgorithms.RsaSha256)], [new EncryptingCredentials(encryption, SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512)]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KeyInfo>> ListAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        return [.. (await db.KeyRing.AsNoTracking().OrderByDescending(k => k.NotAfterUtc).ToListAsync(ct)).Select(ToInfo)];
    }

    /// <inheritdoc />
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        await WithLockAsync(db, async () =>
        {
            if (await db.KeyRing.AnyAsync(ct)) return 0;
            AddPair(db, clock.GetUtcNow(), null);
            await db.SaveChangesAsync(ct);
            log.LogInformation("The first signing and encryption keys were created.");
            return 1;
        }, ct);
        await RefreshAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KeyInfo>> RotateAsync(CancellationToken ct = default)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
            await WithLockAsync(db, async () =>
            {
                var now = clock.GetUtcNow();
                var current = await db.KeyRing.Where(k => k.RetiredAtUtc == null).ToListAsync(ct);
                var latest = await db.KeyRing.Select(k => (DateTimeOffset?)k.NotAfterUtc).MaxAsync(ct);
                foreach (var k in current) { k.RetiredAtUtc = now; k.RemoveAtUtc = now.AddDays(options.Value.RetiredKeyDays); }
                AddPair(db, now, latest);
                await db.SaveChangesAsync(ct);
                return 0;
            }, ct);
        }
        await RefreshAsync(ct);
        return await ListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> RemoveExpiredAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var now = clock.GetUtcNow();
        var removed = await db.KeyRing.Where(k => k.RemoveAtUtc != null && k.RemoveAtUtc < now).ExecuteDeleteAsync(ct);
        if (removed > 0) await RefreshAsync(ct);
        return removed;
    }

    /// <inheritdoc />
    public async Task<bool> RetireAsync(Guid id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var entry = await db.KeyRing.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (entry is null || entry.RetiredAtUtc != null) return false;
        var now = clock.GetUtcNow();
        entry.RetiredAtUtc = now;
        entry.RemoveAtUtc = now.AddDays(options.Value.RetiredKeyDays);
        await db.SaveChangesAsync(ct);
        await RefreshAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Auth2DbContext>();
        var now = clock.GetUtcNow();
        var rows = await db.KeyRing.AsNoTracking().Where(k => k.RemoveAtUtc == null || k.RemoveAtUtc >= now).OrderBy(k => k.Id).ToListAsync(ct);
        var fingerprint = string.Join(',', rows.Select(r => r.Id.ToString("N")));
        if (fingerprint == _snapshot.Fingerprint) return false;

        var keys = new List<(KeyInfo, X509Certificate2)>();
        foreach (var r in rows)
        {
            try
            {
                var pfx = Convert.FromBase64String(Protector.Unprotect(r.ProtectedPfx));
                keys.Add((ToInfo(r), X509CertificateLoader.LoadPkcs12(pfx, null, KeyStorageFlags)));
            }
            catch (CryptographicException ex)
            {
                // Most likely the Data Protection keys changed. The key is skipped; the other keys keep working.
                log.LogError(ex, "A key of the key ring could not be read ({KeyId}). Check that every instance shares the Data Protection keys.", r.Id);
            }
        }

        CancellationTokenSource old;
        lock (_gate)
        {
            _snapshot = new Snapshot(fingerprint, keys);
            old = _changed;
            _changed = new CancellationTokenSource();
        }
        await old.CancelAsync();   // OpenIddict builds its options again with the new keys
        return true;
    }

    private void AddPair(Auth2DbContext db, DateTimeOffset now, DateTimeOffset? latestNotAfter)
    {
        // A later "not after" date makes OpenIddict prefer the newer key, so make sure it is strictly later.
        var notAfter = now.AddDays(options.Value.KeyLifetimeDays);
        if (latestNotAfter is { } latest && notAfter <= latest) notAfter = latest.AddSeconds(1);
        foreach (var purpose in new[] { KeyPurpose.Signing, KeyPurpose.Encryption })
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN=SystemX.Auth2 {purpose} {now:yyyyMMddHHmmss}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                purpose == KeyPurpose.Signing ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, critical: true));
            using var cert = request.CreateSelfSigned(now.AddMinutes(-5), notAfter);   // starts a little earlier: the clocks of instances differ
            db.KeyRing.Add(new KeyRingEntry
            {
                Purpose = purpose,
                ProtectedPfx = Protector.Protect(Convert.ToBase64String(cert.Export(X509ContentType.Pfx))),
                Thumbprint = cert.Thumbprint,
                CreatedAtUtc = now,
                NotAfterUtc = notAfter,
            });
        }
    }

    /// <summary>Runs the work in a transaction that holds a database lock, so two instances never create or rotate keys at the same time.</summary>
    private static Task<T> WithLockAsync<T>(Auth2DbContext db, Func<Task<T>> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended('auth2.keyring', 0))", ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });

    private static KeyInfo ToInfo(KeyRingEntry k) => new(k.Id, k.Purpose.ToString(), k.Thumbprint, k.CreatedAtUtc, k.NotAfterUtc, k.RetiredAtUtc, k.RemoveAtUtc);
}

/// <summary>
/// The validation part of OpenIddict (it checks the tokens of this server) copies the server keys when its options are built,
/// so it must be told about a key change too.
/// </summary>
internal sealed class KeyRingValidationChangeSource(KeyRing ring) : IOptionsChangeTokenSource<global::OpenIddict.Validation.OpenIddictValidationOptions>
{
    public string? Name => Options.DefaultName;
    public Microsoft.Extensions.Primitives.IChangeToken GetChangeToken() => ring.GetChangeToken();
}

/// <summary>
/// Makes the validation options use the current keys of the key ring each time they are built. (The built-in integration copies the
/// server keys once; this keeps the copy right after a rotation.)
/// </summary>
internal sealed class KeyRingValidationConfigurator(KeyRing ring, IOptions<Auth2KeyOptions> keys) : IPostConfigureOptions<global::OpenIddict.Validation.OpenIddictValidationOptions>
{
    public void PostConfigure(string? name, global::OpenIddict.Validation.OpenIddictValidationOptions options)
    {
        if (keys.Value.HasCertificates || options.Configuration is null) return;
        var (signing, encryption) = ring.Credentials();
        options.Configuration.SigningKeys.Clear();
        foreach (var s in signing) options.Configuration.SigningKeys.Add(s.Key);
        options.EncryptionCredentials.Clear();
        foreach (var e in encryption) options.EncryptionCredentials.Add(e);
    }
}

/// <summary>Gives OpenIddict the keys of the key ring, unless certificate files are configured.</summary>
internal sealed class KeyRingOptionsConfigurator(KeyRing ring, IOptions<Auth2KeyOptions> keys) : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        if (keys.Value.HasCertificates) return;
        var (signing, encryption) = ring.Credentials();
        foreach (var s in signing) options.SigningCredentials.Add(s);
        foreach (var e in encryption) options.EncryptionCredentials.Add(e);
    }
}

/// <summary>Looks for keys changed by other instances.</summary>
internal sealed class KeyRingRefreshService(KeyRing ring, IOptions<Auth2KeyOptions> options, ILogger<KeyRingRefreshService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.HasCertificates) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.RefreshSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try { await ring.RefreshAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Could not refresh the key ring."); }
        }
    }
}
