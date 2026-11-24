using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// What a test changes in one go: the project settings and the first application of the project
/// (redirect URIs, allowed scopes and the client settings). The sections of the project settings are forwarded,
/// so a test writes <c>s.Sessions.RequireConsent = true</c> and <c>s.RedirectUris.Add(...)</c> in the same lambda.
/// </summary>
public sealed class TestEdit(ProjectSettings project, ClientApplicationInfo application)
{
    /// <summary>The project settings (changed through the sections below too).</summary>
    public ProjectSettings Project { get; } = project;
    /// <summary>The client settings of the application.</summary>
    public ClientApplicationSettings App { get; } = Persistence.SettingsJson.Clone(application.Settings);
    /// <summary>Allowed redirect URIs (OpenIddict field).</summary>
    public List<string> RedirectUris { get; } = [.. application.RedirectUris];
    /// <summary>Allowed post-logout redirect URIs (OpenIddict field).</summary>
    public List<string> PostLogoutRedirectUris { get; } = [.. application.PostLogoutRedirectUris];
    /// <summary>Allowed scopes (OpenIddict permissions).</summary>
    public List<string> AllowedScopes { get; } = [.. application.AllowedScopes];

    public IdentityMethodSettings Methods => Project.Methods;
    public UserNameSettings UserName => Project.UserName;
    public PasswordSettings Password => Project.Password;
    public EmailSettings Email => Project.Email;
    public PhoneSettings Phone => Project.Phone;
    public RegistrationSettings Registration => Project.Registration;
    public LockoutSettings Lockout => Project.Lockout;
    public TwoFactorSettings TwoFactor => Project.TwoFactor;
    public SessionSettings Sessions => Project.Sessions;
    public RedirectSettings Redirects => Project.Redirects;
    public ClientSettings Clients => Project.Clients;
    public LocalizationSettings Localization => Project.Localization;
    public PasskeySettings Passkeys => Project.Passkeys;
    public List<ExternalProviderSettings> ExternalProviders { get => Project.ExternalProviders; set => Project.ExternalProviders = value; }
    public Contracts.Enums.CredentialScope? CredentialScope { get => Project.CredentialScope; set => Project.CredentialScope = value; }
}
