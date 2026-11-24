using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>
/// A local URL starts with <c>/</c> and not with <c>//</c> or <c>/\</c>. An absolute URL must match the allowed list exactly.
/// Control characters, spaces and backslashes are never accepted, and the check is repeated on the URL-decoded value,
/// so encoded tricks like <c>/%2Fevil.com</c> do not pass.
/// </summary>
public sealed class ReturnUrlValidator : IReturnUrlValidator
{
    /// <inheritdoc />
    public bool IsAllowed(string? url, IEnumerable<string> allowedAbsolute)
    {
        if (string.IsNullOrEmpty(url) || url.Length > 4096) return false;
        if (url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')) return false;

        if (url[0] == '/') return IsLocal(url) && IsLocal(Decode(url));
        return allowedAbsolute.Any(a => string.Equals(a, url, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public string Resolve(string? returnUrl, string? applicationFallback, EffectiveSettings settings, string home = "/")
    {
        var allowed = settings.AllowedRedirectUris;
        if (IsAllowed(returnUrl, allowed)) return returnUrl!;
        if (IsAllowed(applicationFallback, allowed)) return applicationFallback!;
        return home;
    }

    private static bool IsLocal(string url) =>
        url.Length > 0 && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    /// <summary>Decodes up to three times, so double-encoded values are also checked.</summary>
    private static string Decode(string url)
    {
        var current = url;
        for (var i = 0; i < 3; i++)
        {
            string next;
            try { next = Uri.UnescapeDataString(current); }
            catch (UriFormatException) { return "//"; } // not decodable: treat as unsafe
            if (next == current) break;
            current = next;
        }
        // A decoded value that contains a backslash or control character is unsafe, even if the raw value looked fine.
        return current.Any(c => char.IsControl(c) || c == '\\') ? "//" : current;
    }
}
