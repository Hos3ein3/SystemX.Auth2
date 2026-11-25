namespace SystemX.Auth2.Server.Application;

/// <summary>The address of the web server of an application (see <c>Auth2Application.WebServerUrl</c>).</summary>
public static class WebServerUrls
{
    /// <summary>The longest address that is kept.</summary>
    public const int MaxLength = 500;

    /// <summary>
    /// A plain absolute http or https address without a user name, password, query or fragment, written without a trailing slash
    /// (<c>https://shop.example.com/app</c>), or null when the text is empty or not such an address.
    /// </summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxLength) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || string.IsNullOrEmpty(uri.Host)) return null;
        var text = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return text.Length <= MaxLength ? text : null;
    }
}
