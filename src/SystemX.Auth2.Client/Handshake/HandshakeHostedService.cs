using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Handshake;

/// <summary>
/// Makes the handshake when the application starts. What happens on failure is set by
/// <c>OnServerUnavailable</c> and <c>OnApplicationNotFound</c>. With <c>Continue</c> and an unreachable server it retries in the background
/// (2 s, 4 s, ... up to 60 s, 6 tries by default). A wrong configuration (not found, rejected) is never retried.
/// </summary>
public sealed class HandshakeHostedService(HandshakeRunner runner, Auth2ClientState state, IOptions<Auth2ClientOptions> options, TimeProvider clock,
    ILogger<HandshakeHostedService> log) : IHostedService
{
    private CancellationTokenSource? _stop;
    private Task? _retry;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (!o.HandshakeOnStartup) return;

        var outcome = await runner.RunAsync(ct);
        state.Set(outcome.Status, outcome.Response, outcome.Message);
        switch (outcome.Status)
        {
            case HandshakeStatus.Ok:
                log.LogInformation("Handshake with the Auth2 server worked.");
                return;
            case HandshakeStatus.ServerUnavailable:
                if (o.OnServerUnavailable == StartupFailureBehavior.Throw)
                    throw new Auth2StartupException($"The Auth2 server is not available. {outcome.Message}", outcome.Status);
                log.LogWarning("The Auth2 server is not available ({Code}). The application starts anyway and tries again in the background.", outcome.Code);
                _stop = new CancellationTokenSource();
                _retry = Task.Run(() => RetryAsync(_stop.Token), CancellationToken.None);
                return;
            default:
                if (o.OnApplicationNotFound == StartupFailureBehavior.Throw)
                    throw new Auth2StartupException($"The handshake with the Auth2 server failed ({outcome.Status}). {outcome.Message}", outcome.Status);
                log.LogError("The handshake with the Auth2 server failed ({Status}, {Code}): {Message} The application starts anyway; this is a configuration error and is not retried.",
                    outcome.Status, outcome.Code, outcome.Message);
                return;
        }
    }

    private async Task RetryAsync(CancellationToken ct)
    {
        var o = options.Value;
        try
        {
            var delay = o.HandshakeInitialDelay;
            for (var attempt = 1; attempt <= o.HandshakeMaxRetries; attempt++)
            {
                await Task.Delay(delay, clock, ct);
                var outcome = await runner.RunAsync(ct);
                state.Set(outcome.Status, outcome.Response, outcome.Message);
                if (outcome.Status == HandshakeStatus.Ok)
                {
                    log.LogInformation("Handshake with the Auth2 server worked (try {Attempt}).", attempt);
                    return;
                }
                if (outcome.Status != HandshakeStatus.ServerUnavailable)
                {
                    log.LogError("The handshake with the Auth2 server failed ({Status}, {Code}): {Message} Not retrying.", outcome.Status, outcome.Code, outcome.Message);
                    return;
                }
                log.LogWarning("The Auth2 server is still not available (try {Attempt} of {Max}).", attempt, o.HandshakeMaxRetries);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, o.HandshakeMaxDelay.Ticks));
            }
            log.LogError("The Auth2 server could not be reached after {Max} tries. Calls that need the server will fail until it is back; call IAuth2ApiClientService.HandshakeAsync to try again.", o.HandshakeMaxRetries);
        }
        catch (OperationCanceledException) { /* the application is stopping */ }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct)
    {
        if (_stop is null) return;
        await _stop.CancelAsync();
        if (_retry is not null) await Task.WhenAny(_retry, Task.Delay(Timeout.Infinite, ct));
        _stop.Dispose();
    }
}
