using System.Net;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>
/// Every HTTP client that OpenIddict.Client uses (named <c>OpenIddict.Client.SystemNetHttp:...</c>) gets the same protection as the Server's own provider client:
/// https only, no user info in the URL, and no address on a private network (SSRF).
/// </summary>
/// <remarks>
/// OpenIddict accepts only an <c>HttpClientHandler</c> as primary handler, which has no connect callback, so the address is checked just before the request
/// (every address the name resolves to must be public). The small gap between this check and the connection is the price of using the library; the Server's
/// own client (<see cref="ExternalHttp.ClientName"/>, used for SAML metadata and the GitHub calls) checks at connect time.
/// </remarks>
public sealed class ExternalClientHttpFilter(IOptions<Auth2ServerOptions> options) : IHttpMessageHandlerBuilderFilter
{
    /// <inheritdoc />
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name?.StartsWith("OpenIddict.Client", StringComparison.Ordinal) == true)
            builder.AdditionalHandlers.Add(new SsrfGuardHandler(options.Value.ExternalProviders.AllowPrivateNetworks));
    };
}

/// <summary>Refuses a request to a private address or a plain HTTP address (unless private networks are allowed, for development).</summary>
public sealed class SsrfGuardHandler(bool allowPrivate) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri ?? throw new HttpRequestException("The request has no address.");
        SsrfGuard.EnsureAllowedUrl(uri, allowPrivate);
        if (!allowPrivate)
        {
            var addresses = IPAddress.TryParse(uri.Host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(uri.Host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !SsrfGuard.IsPublic(a))) throw new HttpRequestException("The address is not allowed.");
        }
        return await base.SendAsync(request, ct);
    }
}
