using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// Tells the cookie manager which project's session cookie to read. It runs before authentication, when nothing has found the project yet:
/// 1. the client id of the request (<c>client_id</c> / <c>ClientId</c> in the query, or inside <c>ReturnUrl</c>) names the application, which knows its project;
/// 2. else, when the browser sent exactly one session cookie, that cookie's project is meant (pages like <c>/Manage</c> that carry no client id);
/// 3. else nothing is chosen and no session cookie is read: a login in one project never counts for another project on the same domain.
/// </summary>
internal sealed class ProjectCookieMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(HttpContext http, IProjectCookieKey cookieKey, Auth2DbContext db, IMemoryCache cache)
    {
        string? key = null;
        var clientId = FromRequest(http);
        if (!string.IsNullOrEmpty(clientId))
        {
            var cacheKey = "auth2:cookie-project:" + clientId;
            if (!cache.TryGetValue(cacheKey, out string? cached))
            {
                cached = await (from a in db.Set<Auth2Application>().AsNoTracking()
                                join p in db.Projects.AsNoTracking() on a.ProjectId equals p.Id
                                where a.ClientId == clientId
                                select p.ProjectKey).FirstOrDefaultAsync(http.RequestAborted);
                if (cached is not null) cache.Set(cacheKey, cached, Window);   // an unknown client id is not cached
            }
            key = cached;
        }
        if (key is null)
        {
            var path = http.Request.Path.Value ?? "";
            var prefix = Auth2Cookie.CookieName + ".";
            if (path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) && http.Request.Cookies.ContainsKey(prefix + "_global"))
            {
                key = "_global";
            }
            else if (path.StartsWith("/admin/p/", StringComparison.OrdinalIgnoreCase))
            {
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 3)
                {
                    key = segments[2];
                }
            }
        }

        if (key is null)
        {
            var prefix = Auth2Cookie.CookieName + ".";
            var names = http.Request.Cookies.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (names.Count == 1)
            {
                key = names[0][prefix.Length..];
            }
            else if (names.Count > 1)
            {
                // When multiple project session cookies exist on the same host,
                // prefer _global for admin / root routes, or pick the first available.
                if (names.Contains(prefix + "_global"))
                    key = "_global";
                else
                    key = names[0][prefix.Length..];
            }
        }
        cookieKey.CookieProjectKey = key;
        await next(http);
    }

    private static string? FromRequest(HttpContext http)
    {
        var q = http.Request.Query;
        string? Pick(params string[] names) => names.Select(n => q[n].ToString()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        return Pick("client_id", "ClientId") ?? ReturnUrlQuery.Get(Pick("ReturnUrl"), "client_id") ?? ReturnUrlQuery.Get(Pick("ReturnUrl"), "ClientId");
    }
}
