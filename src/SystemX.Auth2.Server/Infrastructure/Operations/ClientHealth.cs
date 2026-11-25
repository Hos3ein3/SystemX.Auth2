using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>
/// See <see cref="IClientHealthService"/>. The registrations and answers are kept in the table <c>server_values</c> (one row per application, key
/// <c>client-health:{application id}</c>), so no migration is needed and every instance sees the same answers.
/// <para>
/// <b>What the Server does with the address.</b> It is an address that an <i>authenticated</i> client gave in its handshake. The Server only sends a plain
/// <c>GET</c> (no cookie, no token, no body), does not follow redirects, reads at most a few kilobytes, and keeps only the status word of the answer.
/// Addresses with a user name or password, or other schemes than http and https, are not accepted.
/// </para>
/// </summary>
internal sealed class ClientHealthService(
    Auth2DbContext db, IHttpClientFactory http, IGlobalScope globalScope, IOptions<Auth2ServerOptions> options, TimeProvider clock, ILogger<ClientHealthService> log) : IClientHealthService
{
    /// <summary>Name of the HTTP client that asks the clients.</summary>
    public const string HttpClientName = "auth2-client-health";

    /// <summary>Key prefix in <c>server_values</c>.</summary>
    public const string KeyPrefix = "client-health:";

    private const int MaxUrlLength = 500;
    private const int MaxBodyBytes = 8192;

    private sealed class Entry
    {
        public string Url { get; set; } = "";
        public string State { get; set; } = nameof(ClientHealthState.Unknown);
        public DateTimeOffset? CheckedAtUtc { get; set; }
        public DateTimeOffset? LastHealthyAtUtc { get; set; }
        public string? Message { get; set; }
    }

    private static string KeyOf(Guid applicationId) => KeyPrefix + applicationId.ToString("N");

    private static Entry? Read(string json)
    {
        try { return JsonSerializer.Deserialize<Entry>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>A plain http or https address without a user name, password or fragment, or null.</summary>
    internal static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)) return null;
        return uri.AbsoluteUri;
    }

    public async Task<bool> RegisterAsync(Guid applicationId, string? healthUrl, CancellationToken ct = default)
    {
        var key = KeyOf(applicationId);
        var row = await db.ServerValues.FirstOrDefaultAsync(v => v.Key == key, ct);
        var url = Normalize(healthUrl);
        if (url is null)
        {
            if (!string.IsNullOrWhiteSpace(healthUrl)) log.LogWarning("The health address of an application was refused: it must be a plain http or https address.");
            if (row is not null) { db.ServerValues.Remove(row); await db.SaveChangesAsync(ct); }
            return false;
        }

        var now = clock.GetUtcNow();
        var entry = row is null ? null : Read(row.Value);
        if (entry is not null && entry.Url == url) return true;   // the same address: keep what we know

        var json = JsonSerializer.Serialize(new Entry { Url = url });
        if (row is null) db.ServerValues.Add(new ServerValue { Key = key, Value = json, UpdatedAtUtc = now });
        else { row.Value = json; row.UpdatedAtUtc = now; }
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> CheckAllAsync(CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var rows = await db.ServerValues.Where(v => v.Key.StartsWith(KeyPrefix)).ToListAsync(ct);
        if (rows.Count == 0) return 0;

        var timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.Jobs.ClientHealthTimeoutSeconds, 1, 60));
        using var gate = new SemaphoreSlim(8);   // at most eight clients at the same time
        var answers = await Task.WhenAll(rows.Select(async row =>
        {
            var entry = Read(row.Value);
            if (entry is null || string.IsNullOrEmpty(entry.Url)) return (Row: row, Entry: (Entry?)null);
            await gate.WaitAsync(ct);
            try { return (Row: row, Entry: await ProbeAsync(entry, timeout, ct)); }
            finally { gate.Release(); }
        }));

        var now = clock.GetUtcNow();
        foreach (var (row, entry) in answers)
        {
            if (entry is null) continue;
            row.Value = JsonSerializer.Serialize(entry);
            row.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        return answers.Count(a => a.Entry is not null);
    }

    private async Task<Entry> ProbeAsync(Entry previous, TimeSpan timeout, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var result = new Entry { Url = previous.Url, CheckedAtUtc = now, LastHealthyAtUtc = previous.LastHealthyAtUtc };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, previous.Url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var code = (int)response.StatusCode;
            string? reported = null;
            if (code == 200 && response.Content.Headers.ContentLength is null or <= MaxBodyBytes)
            {
                var text = await response.Content.ReadAsStringAsync(cts.Token);
                if (text.Length <= MaxBodyBytes) reported = ReadStatus(text);
            }
            result.State = code switch
            {
                200 => reported switch { "Degraded" => nameof(ClientHealthState.Degraded), "Unhealthy" => nameof(ClientHealthState.Unhealthy), _ => nameof(ClientHealthState.Healthy) },
                _ => nameof(ClientHealthState.Unhealthy),
            };
            if (result.State != nameof(ClientHealthState.Healthy)) result.Message = code == 200 ? $"The client reports {reported}." : $"The client answered {code}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            result.State = nameof(ClientHealthState.Unreachable);
            result.Message = ex is HttpRequestException ? "The client could not be reached." : $"The client did not answer in {timeout.TotalSeconds:0} seconds.";
        }
        if (result.State == nameof(ClientHealthState.Healthy)) result.LastHealthyAtUtc = now;
        log.LogDebug("Health of {Url}: {State} in {Milliseconds} ms.", previous.Url, result.State, watch.ElapsedMilliseconds);
        return result;
    }

    private static string? ReadStatus(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task<IReadOnlyList<ClientHealthInfo>> GetProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var applications = await db.Set<Auth2Application>().AsNoTracking().Where(a => a.ProjectId == projectId).Select(a => new { a.Id, a.Name }).ToListAsync(ct);
        if (applications.Count == 0) return [];
        var keys = applications.Select(a => KeyOf(a.Id)).ToList();
        var rows = await db.ServerValues.AsNoTracking().Where(v => keys.Contains(v.Key)).ToListAsync(ct);

        // An answer older than three rounds means that the checker is not running: say "unknown" instead of a state that may be wrong.
        var stale = TimeSpan.FromSeconds(Math.Max(10, options.Value.Jobs.ClientHealthIntervalSeconds) * 3 + options.Value.Jobs.ClientHealthTimeoutSeconds);
        var now = clock.GetUtcNow();
        var list = new List<ClientHealthInfo>();
        foreach (var app in applications.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = rows.FirstOrDefault(r => r.Key == KeyOf(app.Id));
            if (row is null || Read(row.Value) is not { } entry) continue;
            var state = Enum.TryParse<ClientHealthState>(entry.State, out var parsed) ? parsed : ClientHealthState.Unknown;
            if (entry.CheckedAtUtc is null || now - entry.CheckedAtUtc > stale) state = ClientHealthState.Unknown;
            list.Add(new ClientHealthInfo(app.Id, app.Name ?? "", state, entry.CheckedAtUtc, entry.LastHealthyAtUtc, entry.Message));
        }
        return list;
    }
}

/// <summary>Asks the clients with a two-way health check every minute (<c>Auth2:Jobs:ClientHealthIntervalSeconds</c>). Only one instance does it at a time.</summary>
internal sealed class ClientHealthMonitor(IServiceScopeFactory scopes, IDistributedLock locks, IOptions<Auth2ServerOptions> options, ILogger<ClientHealthMonitor> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jobs = options.Value.Jobs;
        if (!jobs.Enabled || !jobs.ClientHealthEnabled) return;
        var interval = TimeSpan.FromSeconds(Math.Max(10, jobs.ClientHealthIntervalSeconds));
        try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, interval.TotalSeconds)), stoppingToken); }   // let the clients finish their start first
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await locks.TryRunAsync("client-health", async token =>
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IClientHealthService>().CheckAllAsync(token);
                }, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "The health check of the clients failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
