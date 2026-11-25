using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Options;

namespace SystemX.Auth2.Client.HealthChecks;

/// <summary>What the handshake tells the Server about the two-way health check.</summary>
/// <param name="Url">The whole address (when <c>PublicUrl</c> is set), or null.</param>
/// <param name="Port">The port this application listens on (when there is no <c>PublicUrl</c>), or null.</param>
/// <param name="Scheme">The scheme that goes with <paramref name="Port"/>, or null.</param>
/// <param name="Path">The path where this application answers (with <paramref name="Port"/>), or null.</param>
public sealed record HealthAddress(string? Url, int? Port, string? Scheme, string? Path)
{
    /// <summary>One-way, or two-way that could not find an address: the handshake carries nothing.</summary>
    public static HealthAddress None { get; } = new(null, null, null, null);
}

/// <summary>
/// Finds what to tell the Server for a two-way health check. With <c>PublicUrl</c>: the whole address. Without it: only the <b>port</b> (and the scheme and path) this application
/// listens on, found, in this order, in the server addresses of the web server (known once it runs), <c>urls</c> / <c>ASPNETCORE_URLS</c>, and <c>http_ports</c> /
/// <c>ASPNETCORE_HTTP_PORTS</c>. The Server then takes the host from the network address of the handshake. Only <b>http</b> is used for this: a certificate does not match an IP address.
/// </summary>
public sealed class HealthAddressResolver(IOptions<Auth2HealthCheckOptions> options, IConfiguration configuration, IServiceProvider services, ILogger<HealthAddressResolver> log)
{
    private bool _warned;

    /// <summary>The address for the next handshake.</summary>
    public HealthAddress Resolve()
    {
        var o = options.Value;
        if (o.Mode != Auth2HealthCheckMode.TwoWay) return HealthAddress.None;
        if (o.HealthUrl is { } url) return new HealthAddress(url, null, null, o.Path);   // the path also tells the Server where to ask when it knows the web server address

        var port = FindHttpPort(out var onlyHttps);
        if (port is not null) return new HealthAddress(null, port, Uri.UriSchemeHttp, o.Path);

        if (!_warned)
        {
            _warned = true;
            log.LogWarning(onlyHttps
                ? "The two-way health check needs PublicUrl: this application listens only on https, and the server cannot call an IP address with a certificate. Set EnableHealthCheck(o => o.PublicUrl = ...)."
                : "The two-way health check could not find the port of this application (no ASPNETCORE_URLS, urls or http port). Set EnableHealthCheck(o => o.PublicUrl = ...).");
        }
        return HealthAddress.None;
    }

    private int? FindHttpPort(out bool onlyHttps)
    {
        var addresses = new List<string>();
        try { addresses.AddRange(services.GetService(typeof(IServer)) is IServer server ? server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [] : []); }
        catch (ObjectDisposedException) { /* the host is stopping */ }
        foreach (var key in new[] { "urls", "ASPNETCORE_URLS" })
            if (configuration[key] is { Length: > 0 } urls) addresses.AddRange(urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var sawHttps = false;
        foreach (var address in addresses)
        {
            // "http://+:5001", "http://*:5001" and "http://0.0.0.0:80" are "every address": only the port matters here
            var text = address.Replace("://+", "://localhost").Replace("://*", "://localhost");
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Port <= 0) continue;
            if (uri.Scheme == Uri.UriSchemeHttp) { onlyHttps = false; return uri.Port; }
            if (uri.Scheme == Uri.UriSchemeHttps) sawHttps = true;
        }

        foreach (var key in new[] { "http_ports", "HTTP_PORTS", "ASPNETCORE_HTTP_PORTS" })
            if (configuration[key] is { Length: > 0 } ports)
                foreach (var part in ports.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (int.TryParse(part, out var port) && port is > 0 and <= 65535) { onlyHttps = false; return port; }

        onlyHttps = sawHttps || configuration["https_ports"] is { Length: > 0 } || configuration["HTTPS_PORTS"] is { Length: > 0 } || configuration["ASPNETCORE_HTTPS_PORTS"] is { Length: > 0 };
        return null;
    }
}
