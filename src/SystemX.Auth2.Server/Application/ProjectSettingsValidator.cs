using System.Text.RegularExpressions;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>One validation error. <c>Path</c> is like <c>Password.MinLength</c>.</summary>
public sealed record SettingsError(string Path, string Message);

/// <summary>Rejects unsafe or broken project settings.</summary>
public static class ProjectSettingsValidator
{
    /// <summary>Smallest allowed password length.</summary>
    public const int AbsoluteMinPasswordLength = 6;

    /// <summary>Validates settings. Returns an empty list when all is fine.</summary>
    public static IReadOnlyList<SettingsError> Validate(ProjectSettings s)
    {
        var e = new List<SettingsError>();
        void Err(string path, string msg) => e.Add(new SettingsError(path, msg));

        if (s.SchemaVersion < 1 || s.SchemaVersion > ProjectSettings.CurrentSchemaVersion)
            Err("SchemaVersion", "Unknown schema version.");

        if (s.Methods.AllowedLoginMethods.Count == 0) Err("Methods.AllowedLoginMethods", "At least one login method is needed.");

        var u = s.UserName;
        if (u.MinLength < 1) Err("UserName.MinLength", "Must be at least 1.");
        if (u.MaxLength < u.MinLength) Err("UserName.MaxLength", "Must not be less than MinLength.");
        if (u.MaxLength > 256) Err("UserName.MaxLength", "Must not be more than 256.");
        CheckRegex("UserName.AllowedCharacters", u.AllowedCharacters, 200, Err);

        var p = s.Password;
        if (p.MinLength < AbsoluteMinPasswordLength) Err("Password.MinLength", $"Must be at least {AbsoluteMinPasswordLength}.");
        if (p.MaxLength < p.MinLength) Err("Password.MaxLength", "Must not be less than MinLength.");
        if (p.MaxLength > 1024) Err("Password.MaxLength", "Must not be more than 1024.");
        if (p.UniqueChars < 0 || p.UniqueChars > p.MinLength) Err("Password.UniqueChars", "Must be between 0 and MinLength.");

        CheckRegex("Phone.PhoneNumberRegex", s.Phone.PhoneNumberRegex, s.Phone.RegexTimeoutMs, Err);
        if (s.Phone.RegexTimeoutMs is < 1 or > 1000) Err("Phone.RegexTimeoutMs", "Must be between 1 and 1000. A regex always needs a timeout.");

        var l = s.Lockout;
        if (l.MaxFailedAttempts < 1) Err("Lockout.MaxFailedAttempts", "Must be at least 1.");
        if (l.LockoutMinutes < 1) Err("Lockout.LockoutMinutes", "Must be at least 1.");

        var t = s.TwoFactor;
        if (t.OtpLength is < 4 or > 10) Err("TwoFactor.OtpLength", "Must be between 4 and 10.");
        if (t.OtpLifetimeSeconds is < 10 or > 3600) Err("TwoFactor.OtpLifetimeSeconds", "Must be between 10 and 3600.");
        if (t.OtpMaxAttempts < 1) Err("TwoFactor.OtpMaxAttempts", "Must be at least 1.");
        if (t.OtpResendSeconds < 0) Err("TwoFactor.OtpResendSeconds", "Must not be negative.");
        if (t.RecoveryCodeCount is < 1 or > 50) Err("TwoFactor.RecoveryCodeCount", "Must be between 1 and 50.");
        if (t.Mode == TwoFactorMode.Required && !t.AllowTotp)
            Err("TwoFactor.Mode", "Required two-factor needs AllowTotp.");

        var se = s.Sessions;
        if (se.AccessTokenMinutes is < 1 or > 1440) Err("Sessions.AccessTokenMinutes", "Must be between 1 and 1440.");
        if (se.RefreshTokenDays is < 1 or > 365) Err("Sessions.RefreshTokenDays", "Must be between 1 and 365.");
        if (se.IdentityTokenMinutes is < 1 or > 1440) Err("Sessions.IdentityTokenMinutes", "Must be between 1 and 1440.");
        if (se.CookieLifetimeMinutes is < 1) Err("Sessions.CookieLifetimeMinutes", "Must be at least 1 when set.");

        foreach (var (path, value) in new[]
        {
            ("Redirects.RedirectAfterLogin", s.Redirects.RedirectAfterLogin),
            ("Redirects.RedirectAfterRegister", s.Redirects.RedirectAfterRegister),
            ("Redirects.RedirectAfterLogout", s.Redirects.RedirectAfterLogout),
        })
        {
            if (!string.IsNullOrEmpty(value) && !IsLocalPath(value) && !IsSafeAbsoluteUri(value))
                Err(path, "Must be a local path (starting with a single '/') or an absolute http(s) URL.");
        }

        var lo = s.Localization;
        if (string.IsNullOrWhiteSpace(lo.DefaultCulture)) Err("Localization.DefaultCulture", "Is required.");
        else if (!lo.SupportedCultures.Contains(lo.DefaultCulture, StringComparer.OrdinalIgnoreCase))
            Err("Localization.DefaultCulture", "Must be one of SupportedCultures.");

        ExternalProviderSettingsValidator.Validate(s, Err);

        return e;
    }

    /// <summary>Throws <see cref="SettingsValidationException"/> when the settings are not valid.</summary>
    public static void ValidateOrThrow(ProjectSettings s)
    {
        var errors = Validate(s);
        if (errors.Count > 0) throw new SettingsValidationException(errors);
    }

    private static void CheckRegex(string path, string? pattern, int timeoutMs, Action<string, string> err)
    {
        if (string.IsNullOrEmpty(pattern)) { err(path, "Is required."); return; }
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 1, 1000)));
        }
        catch (ArgumentException)
        {
            err(path, "Is not a valid regular expression.");
        }
    }

    /// <summary>Checks that every URI is absolute, http(s), without wildcards and without a fragment.</summary>
    public static void CheckUris(string path, IEnumerable<string> uris, Action<string, string> err)
    {
        foreach (var uri in uris)
        {
            if (uri.Contains('*')) err(path, $"'{uri}' has a wildcard. Wildcards are not allowed.");
            else if (!IsSafeAbsoluteUri(uri) || uri.Contains('#')) err(path, $"'{uri}' must be an absolute http(s) URL without a fragment.");
        }
    }

    internal static bool IsSafeAbsoluteUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static bool IsLocalPath(string value) =>
        value.Length > 0 && value[0] == '/' && (value.Length == 1 || (value[1] != '/' && value[1] != '\\'));
}

/// <summary>Rejects unsafe or broken application (client) settings.</summary>
public static class ClientApplicationSettingsValidator
{
    /// <summary>Validates the settings of one application and reports every problem.</summary>
    public static void Validate(ClientApplicationSettings s, Action<string, string> err)
    {
        if (s.SchemaVersion < 1 || s.SchemaVersion > ClientApplicationSettings.CurrentSchemaVersion) err("SchemaVersion", "Unknown schema version.");
        if (s.AccessTokenMinutes is { } a && (a < 1 || a > 1440)) err("AccessTokenMinutes", "Must be between 1 and 1440.");
        if (s.RefreshTokenDays is { } r && (r < 1 || r > 365)) err("RefreshTokenDays", "Must be between 1 and 365.");
        if (s.IdentityTokenMinutes is { } i && (i < 1 || i > 1440)) err("IdentityTokenMinutes", "Must be between 1 and 1440.");
        if (s.CookieLifetimeMinutes is < 1) err("CookieLifetimeMinutes", "Must be at least 1 when set.");
        foreach (var origin in s.AllowedCorsOrigins)
            if (origin.Contains('*') || !ProjectSettingsValidator.IsSafeAbsoluteUri(origin))
                err("AllowedCorsOrigins", $"'{origin}' must be an absolute http(s) origin without wildcards.");
    }
}
