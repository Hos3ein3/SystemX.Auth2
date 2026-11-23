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
    public string Error(string key, params object[] args) =>
        args.Length == 0 ? errors[key].Value : errors[key, args].Value;   // without arguments the text is returned as it is (no format error)

    /// <inheritdoc />
    public string Validation(string key, params object[] args) =>
        args.Length == 0 ? validation[key].Value : validation[key, args].Value;
}
