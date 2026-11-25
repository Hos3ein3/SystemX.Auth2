using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using SystemX.Auth2.Client;
using SystemX.Auth2.Client.Machine;
using SystemX.Auth2.Client.Services;

namespace SystemX.Auth2.Samples.Worker;

/// <summary>Options of the job (section <c>Job</c>).</summary>
public sealed class JobOptions
{
    /// <summary>Seconds between two runs. Default 30.</summary>
    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>
/// The job. Every <see cref="JobOptions.IntervalSeconds"/> it does two things as a program:
/// <list type="number">
/// <item>calls another API (the MinimalApi sample) with a machine token: <c>/api/machine-only</c> (machine tokens only) and <c>/api/reports</c> (needs the scope <c>reports.read</c>);</item>
/// <item>calls the Auth2 Server through <see cref="IAuth2ApiClientService"/>, which uses a machine token of its own (here: reads the permission catalog).</item>
/// </list>
/// It never stops for an error: a failed run is logged and the next run tries again.
/// </summary>
public sealed class ReportJob(IHttpClientFactory http, IAuth2MachineTokenProvider tokens, IAuth2ApiClientService auth2, IOptions<JobOptions> options, ILogger<ReportJob> log) : BackgroundService
{
    /// <summary>The name of the <c>HttpClient</c> that carries the machine token.</summary>
    public const string ApiClientName = "minimal-api";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.IntervalSeconds)));
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await ShowTheTokenAsync(ct);
            await CallTheApiAsync(ct);
            await CallTheAuth2ServerAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Auth2Exception ex)
        {
            // The token could not be made: the secret is wrong, the scope is not allowed (not transient), or the server is down (transient).
            log.LogWarning("The job could not get a machine token ({Code}, transient: {Transient}). It tries again at the next run.", ex.ErrorCode, ex.IsTransient);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "The job failed. It tries again at the next run.");
        }
    }

    /// <summary>Shows what the token looks like (the scopes and the audiences). The token is cached, so this is not a new request each time.</summary>
    private async Task ShowTheTokenAsync(CancellationToken ct)
    {
        var token = new JsonWebToken(await tokens.GetTokenAsync(["reports.read", "jobs.run"], ct));
        log.LogInformation("Machine token: client {ClientId}, scopes [{Scopes}], audiences [{Audiences}], expires {Expires:u}",
            token.GetClaim("client_id")?.Value, string.Join(' ', token.Claims.Where(c => c.Type == "scope").SelectMany(c => c.Value.Split(' '))), string.Join(", ", token.Audiences), token.ValidTo);
    }

    private async Task CallTheApiAsync(CancellationToken ct)
    {
        var client = http.CreateClient(ApiClientName);   // every request has the machine token
        foreach (var path in new[] { "api/machine-only", "api/reports" })
        {
            using var response = await client.GetAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            log.LogInformation("GET {Path} -> {Status} {Body}", path, (int)response.StatusCode, body.Length > 120 ? body[..120] + "..." : body);
        }
    }

    private async Task CallTheAuth2ServerAsync(CancellationToken ct)
    {
        // IAuth2ApiClientService asks for the scope that each call needs (here auth2.permissions.sync) and caches the token.
        var permissions = await auth2.GetPermissionsAsync(ct);
        if (permissions.IsSuccess) log.LogInformation("The Auth2 server has {Count} permissions in this project.", permissions.Value!.Count);
        else log.LogWarning("Reading the permissions failed: {Code}", permissions.Error!.Code);
    }
}
