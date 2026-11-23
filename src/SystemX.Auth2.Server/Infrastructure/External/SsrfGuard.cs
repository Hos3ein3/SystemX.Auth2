using System.Net;
using System.Net.Sockets;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>
/// Stops the Server from being used to reach private networks (SSRF). Used for every URL that an admin or a provider document can set
/// (discovery, metadata, token and user info endpoints). The check runs at connect time, on the real IP address, so DNS tricks do not help.
/// </summary>
public static class SsrfGuard
{
    /// <summary>True when the address is public and may be contacted.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal) return false;
            return true;
        }
        var b = address.GetAddressBytes();
        return !(b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)            // link local, cloud metadata (169.254.169.254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127) // carrier grade NAT
            || b[0] == 0 || b[0] >= 224);
    }

    /// <summary>Checks the form of a URL before any request: https, no user info.</summary>
    public static void EnsureAllowedUrl(Uri uri, bool allowInsecure)
    {
        if (!uri.IsAbsoluteUri) throw new InvalidOperationException("The URL must be absolute.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidOperationException("The URL must not contain a user name or password.");
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowInsecure && uri.Scheme == Uri.UriSchemeHttp))
            throw new InvalidOperationException("The URL must use https.");
    }

    /// <summary>A connect callback for <see cref="SocketsHttpHandler"/>: resolves the name, refuses non-public addresses, then connects.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct, bool allowPrivate = false)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        var allowed = addresses.Where(a => allowPrivate || IsPublic(a)).ToArray();
        if (allowed.Length == 0) throw new HttpRequestException("The address is not allowed.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
