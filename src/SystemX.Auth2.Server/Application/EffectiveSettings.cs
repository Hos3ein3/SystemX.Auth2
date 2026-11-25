using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>
/// The value that counts for one request: the project value, overridden by the application value where the application sets one.
/// This is the only place where the precedence is written down.
/// </summary>
public sealed class EffectiveSettings(ProjectSettings project, ClientApplicationInfo? application)
{
    /// <summary>The project level settings.</summary>
    public ProjectSettings Project { get; } = project;
    /// <summary>The calling application, or null when the request has no client.</summary>
    public ClientApplicationInfo? Application { get; } = application;

    private ClientApplicationSettings? App => Application?.Settings;

    /// <summary>UI mode: application, else the project default.</summary>
    public UiMode UiMode => App?.UiMode ?? Project.Clients.DefaultUiMode;
    /// <summary>Cookie lifetime in minutes: application, else project, else null (session cookie).</summary>
    public int? CookieLifetimeMinutes => App?.CookieLifetimeMinutes ?? Project.Sessions.CookieLifetimeMinutes;
    /// <summary>Access token lifetime in minutes.</summary>
    public int AccessTokenMinutes => App?.AccessTokenMinutes ?? Project.Sessions.AccessTokenMinutes;
    /// <summary>Refresh token lifetime in days.</summary>
    public int RefreshTokenDays => App?.RefreshTokenDays ?? Project.Sessions.RefreshTokenDays;
    /// <summary>Identity token lifetime in minutes.</summary>
    public int IdentityTokenMinutes => App?.IdentityTokenMinutes ?? Project.Sessions.IdentityTokenMinutes;
    /// <summary>Consent mode: application, else project.</summary>
    public ConsentMode ConsentMode => App?.ConsentMode ?? ConsentPolicy.Effective(Project);
    /// <summary>True when this client may use the password grant and the two-factor grants (Self UI).</summary>
    public bool AllowSelfUiLogin => App?.AllowSelfUiLogin ?? false;
    /// <summary>Redirect URIs of the application (OpenIddict field). Empty when the request has no client.</summary>
    public IReadOnlyList<string> AllowedRedirectUris => Application?.RedirectUris ?? [];

    /// <summary>True when the confirmation email is sent right after registration: application, else project; always true when a confirmed email is required.</summary>
    public bool SendConfirmationEmail => Project.Email.RequireConfirmedEmail || (App?.SendConfirmationEmail ?? Project.Registration.SendConfirmationEmail);
    /// <summary>True when the confirmation SMS is sent right after registration: application, else project; always true when a confirmed phone is required.</summary>
    public bool SendConfirmationSMS => Project.Phone.RequireConfirmedPhone || (App?.SendConfirmationSMS ?? Project.Registration.SendConfirmationSMS);
    /// <summary>The email provider chosen for this request: application, else project. Null means "use the server default".</summary>
    public EmailProviderSettings? EmailProvider => Pick(App?.Messaging?.Email, Project.Messaging.Email);
    /// <summary>The SMS provider chosen for this request: application, else project. Null means "use the server default".</summary>
    public SmsProviderSettings? SmsProvider => App?.Messaging?.Sms is { Provider.Length: > 0 } a ? a : Project.Messaging.Sms is { Provider.Length: > 0 } p ? p : null;

    private static EmailProviderSettings? Pick(EmailProviderSettings? application, EmailProviderSettings project) =>
        application is { Provider.Length: > 0 } ? application : project is { Provider.Length: > 0 } ? project : null;

    /// <summary>The external providers this client shows: a subset of the project's providers (all when the application lists none).</summary>
    public IEnumerable<T> FilterProviders<T>(IEnumerable<T> projectProviders, Func<T, string> name)
    {
        var subset = App?.ExternalProviders;
        return subset is null ? projectProviders : projectProviders.Where(p => subset.Contains(name(p), StringComparer.Ordinal));
    }
}

/// <summary>Scopes a new application may request unless the admin says otherwise.</summary>
public static class ClientScopeDefaults
{
    /// <summary>Default allowed scopes of a new application.</summary>
    public static IReadOnlyList<string> DefaultScopes { get; } =
        ["openid", "profile", "email", "phone", "roles", "offline_access", "auth2.me", "auth2.client.handshake", "auth2.register", "auth2.login",
         "auth2.permissions.sync", "auth2.roles.sync", "auth2.groups.read", "auth2.groups.sync", "auth2.scopes.sync"];
}
