using Microsoft.Extensions.Localization;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Resources;

namespace SystemX.Auth2.Server.Infrastructure.Localization;

/// <summary>
/// Texts come from resx only. Lookup: resx of the current UI culture, then the English (neutral) resx, then the key itself.
/// The .NET resource manager does the culture fallback; a missing key returns the key.
/// </summary>
public sealed class Auth2Localizer(IStringLocalizer<Errors> errors, IStringLocalizer<Validation> validation) : IAuth2Localizer
{
    /// <inheritdoc />
    public string Error(string key, params object[] args) => errors[key, args].Value;

    /// <inheritdoc />
    public string Validation(string key, params object[] args) => validation[key, args].Value;
}
