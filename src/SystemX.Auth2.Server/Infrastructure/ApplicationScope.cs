using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>Scoped holder of the current application and the global-scope switch. Registered as scoped.</summary>
public sealed class ApplicationScope : IApplicationScope, IGlobalScope, IClientIdAccessor, IApplicationSelection
{
    private int _globalDepth;

    /// <inheritdoc />
    public Guid? ApplicationId { get; private set; }

    /// <inheritdoc />
    public bool IsGlobalActive => Volatile.Read(ref _globalDepth) > 0;

    /// <inheritdoc />
    bool IGlobalScope.IsActive => IsGlobalActive;

    /// <inheritdoc />
    public string? ClientId { get; set; }

    /// <inheritdoc />
    public string? ProjectKey { get; set; }

    /// <inheritdoc />
    public void Set(Guid applicationId) => ApplicationId = applicationId;

    /// <inheritdoc />
    public IDisposable Begin()
    {
        Interlocked.Increment(ref _globalDepth);
        return new Releaser(this);
    }

    private sealed class Releaser(ApplicationScope owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref owner._globalDepth);
        }
    }
}
