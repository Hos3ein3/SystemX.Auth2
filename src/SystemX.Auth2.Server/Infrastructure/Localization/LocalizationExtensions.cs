using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;

namespace SystemX.Auth2.Server.Infrastructure.Localization;

/// <summary>Request localization setup.</summary>
public static class LocalizationExtensions
{
    /// <summary>
    /// Adds request localization with the Auth2 culture order (see <see cref="Auth2CultureResolver"/>).
    /// Supported cultures are the server's resx cultures.
    /// </summary>
    public static IApplicationBuilder UseAuth2Localization(this IApplicationBuilder app)
    {
        var cultures = Auth2CultureResolver.ServerCultures.Select(c => new CultureInfo(c)).ToList();
        return app.UseRequestLocalization(o =>
        {
            o.DefaultRequestCulture = new RequestCulture(Auth2CultureResolver.Fallback);
            o.SupportedCultures = cultures;
            o.SupportedUICultures = cultures;
            o.RequestCultureProviders.Clear();
            o.RequestCultureProviders.Add(new Auth2RequestCultureProvider());
        });
    }
}
