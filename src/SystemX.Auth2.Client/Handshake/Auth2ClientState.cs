using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Handshake;

/// <summary>Thrown at start-up when the handshake fails and the options say the application must not start.</summary>
public sealed class Auth2StartupException(string message, HandshakeStatus status, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Why the start-up failed.</summary>
    public HandshakeStatus Status { get; } = status;
}

/// <summary>What the client knows about the server. One instance for the whole application.</summary>
public sealed class Auth2ClientState
{
    private readonly object _gate = new();
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HandshakeStatus? _status;
    private ClientHandshakeResponse? _response;
    private string? _message;

    /// <summary>The result of the last handshake, or null while none has finished.</summary>
    public HandshakeStatus? Status { get { lock (_gate) return _status; } }

    /// <summary>The answer of the last successful handshake.</summary>
    public ClientHandshakeResponse? LastResponse { get { lock (_gate) return _response; } }

    /// <summary>A short explanation of the last failure (never holds secrets), or null.</summary>
    public string? Message { get { lock (_gate) return _message; } }

    /// <summary>True when the last handshake worked.</summary>
    public bool IsReady => Status == HandshakeStatus.Ok;

    /// <summary>Records a result. Wakes everybody waiting for <see cref="WaitUntilReadyAsync"/> when it is <see cref="HandshakeStatus.Ok"/>.</summary>
    public void Set(HandshakeStatus status, ClientHandshakeResponse? response = null, string? message = null)
    {
        TaskCompletionSource? toComplete = null;
        lock (_gate)
        {
            _status = status;
            _response = status == HandshakeStatus.Ok ? response : _response;
            _message = status == HandshakeStatus.Ok ? null : message;
            if (status == HandshakeStatus.Ok) toComplete = _ready;
            else if (_ready.Task.IsCompleted) _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        toComplete?.TrySetResult();
    }

    /// <summary>Waits until a handshake worked. Sync services use it. Stops with an exception when <paramref name="ct"/> is cancelled.</summary>
    public Task WaitUntilReadyAsync(CancellationToken ct = default)
    {
        Task task;
        lock (_gate) task = _ready.Task;
        return task.WaitAsync(ct);
    }
}
