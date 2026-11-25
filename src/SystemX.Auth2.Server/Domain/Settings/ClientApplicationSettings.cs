using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Server.Domain.Settings;

/// <summary>
/// Settings of one application (client). Stored in <c>SettingsJson</c> next to the OpenIddict fields.
/// It holds only what OpenIddict has no field for. A null value means "use the project value".
/// Redirect URIs, allowed scopes, client type and consent type are OpenIddict fields and are not repeated here.
/// </summary>
public sealed class ClientApplicationSettings
{
    /// <summary>Current JSON schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the stored JSON.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Which UI the client uses (hosted, self, none). Null: the project default.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public UiMode? UiMode { get; set; }
    /// <summary>Cookie lifetime in minutes. Null: the project value.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int? CookieLifetimeMinutes { get; set; }
    /// <summary>Access token lifetime in minutes. Null: the project value.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int? AccessTokenMinutes { get; set; }
    /// <summary>Refresh token lifetime in days. Null: the project value.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int? RefreshTokenDays { get; set; }
    /// <summary>Identity token lifetime in minutes. Null: the project value.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int? IdentityTokenMinutes { get; set; }
    /// <summary>Allowed CORS origins of this client. CORS is off when empty.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public List<string> AllowedCorsOrigins { get; set; } = [];
    /// <summary>Enables the password grant and the two-factor grants for this client (Self UI).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowSelfUiLogin { get; set; }
    /// <summary>
    /// This application's client may tell the Server the address of its web server and its application type in the handshake (<c>Auth2ClientOptions.WebServerUrl</c>, <c>ApplicationType</c>),
    /// and the Server stores them on the application. On (default): the client's value replaces the stored one. Off: only a global admin sets them, and the client's value is ignored (the handshake answer says <c>webServerLocked</c>).
    /// </summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public bool AllowClientToSetWebServerUrl { get; set; } = true;
    /// <summary>Names of the project's external providers this client shows. Null: all of them. A subset only; it never adds a provider.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public List<string>? ExternalProviders { get; set; }
    /// <summary>Consent mode. Null: the project value.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public ConsentMode? ConsentMode { get; set; }
    /// <summary>Send the confirmation email after registration through this application. Null: the project value.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public bool? SendConfirmationEmail { get; set; }
    /// <summary>Send the confirmation SMS after registration through this application. Null: the project value.</summary>
    [SettingEditability(SettingEditor.ProjectAdminEditable)] public bool? SendConfirmationSMS { get; set; }
    /// <summary>This application's own email / SMS providers. Null: the project's. Changed only through the messaging admin routes.</summary>
    public MessagingSettings? Messaging { get; set; }
}
