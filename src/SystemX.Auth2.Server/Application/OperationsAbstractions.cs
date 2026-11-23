namespace SystemX.Auth2.Server.Application;

/// <summary>Public facts about one key of the key ring. The private key is never shown.</summary>
public sealed record KeyInfo(Guid Id, string Purpose, string Thumbprint, DateTimeOffset CreatedAtUtc, DateTimeOffset NotAfterUtc,
    DateTimeOffset? RetiredAtUtc, DateTimeOffset? RemoveAtUtc);

/// <summary>
/// Signing and encryption keys kept by the Server (when no certificate files are configured).
/// Rotation adds a new pair and retires the old one; retired keys keep checking old tokens until they are removed.
/// </summary>
public interface IKeyRing
{
    /// <summary>All keys that exist now.</summary>
    Task<IReadOnlyList<KeyInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates the first signing and encryption key when there are none. Safe with several instances.</summary>
    Task EnsureAsync(CancellationToken ct = default);

    /// <summary>Makes a new pair, retires the current one, and makes this instance use the new keys at once.</summary>
    Task<IReadOnlyList<KeyInfo>> RotateAsync(CancellationToken ct = default);

    /// <summary>Deletes retired keys whose time is over. Returns how many.</summary>
    Task<int> RemoveExpiredAsync(CancellationToken ct = default);

    /// <summary>Reads the keys again. Returns true when they changed (another instance rotated).</summary>
    Task<bool> RefreshAsync(CancellationToken ct = default);
}

/// <summary>Runs work so that only one instance does it at a time (a lock in the database).</summary>
public interface IDistributedLock
{
    /// <summary>Runs <paramref name="work"/> when the lock is free. Returns false (and does nothing) when another instance holds it.</summary>
    Task<bool> TryRunAsync(string name, Func<CancellationToken, Task> work, CancellationToken ct = default);
}

/// <summary>Tells every instance that the settings of an application changed (Redis pub/sub when Redis is on).</summary>
public interface ISettingsInvalidationBus
{
    /// <summary>Announces a change. Other instances drop their cached settings of the application.</summary>
    Task PublishAsync(Guid applicationId, CancellationToken ct = default);
}
