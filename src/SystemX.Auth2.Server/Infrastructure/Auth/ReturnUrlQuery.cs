using Microsoft.AspNetCore.WebUtilities;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>Reads a query value from a return URL such as <c>/connect/authorize?client_id=x&amp;ui_locales=nl</c>.</summary>
public static class ReturnUrlQuery
{
    /// <summary>Returns the value, or null.</summary>
    public static string? Get(string? returnUrl, string key)
    {
        if (string.IsNullOrEmpty(returnUrl)) return null;
        var q = returnUrl.IndexOf('?', StringComparison.Ordinal);
        if (q < 0) return null;
        var values = QueryHelpers.ParseQuery(returnUrl[(q + 1)..]);
        return values.TryGetValue(key, out var v) && v.Count > 0 ? v.ToString() : null;
    }
}
