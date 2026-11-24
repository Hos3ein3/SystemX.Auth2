namespace SystemX.Auth2.Server.Domain.Settings;

/// <summary>How an SMTP connection is secured. Written as text in JSON.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum SmtpSecurity
{
    /// <summary>Start in clear text and upgrade with STARTTLS when the server offers it (port 587).</summary>
    StartTls = 0,
    /// <summary>TLS from the first byte (port 465).</summary>
    SslOnConnect = 1,
    /// <summary>No encryption. Only for a trusted local relay.</summary>
    None = 2,
}

/// <summary>Names of the email providers that ship with the server. Your own provider is any other name you register.</summary>
public static class EmailProviders
{
    /// <summary>Empty: use the next level (application, then project, then the server default).</summary>
    public const string Inherit = "";
    /// <summary>Writes a log line only (development).</summary>
    public const string Logging = "logging";
    /// <summary>Any SMTP server.</summary>
    public const string Smtp = "smtp";
    /// <summary>Gmail / Google Workspace SMTP (smtp.gmail.com). Needs an app password.</summary>
    public const string Google = "google";
}

/// <summary>Names of the SMS providers that ship with the server.</summary>
public static class SmsProviders
{
    /// <summary>Empty: use the next level.</summary>
    public const string Inherit = "";
    /// <summary>Writes a log line only (development).</summary>
    public const string Logging = "logging";
}

/// <summary>How emails are sent. The provider is a name; the other fields are used by the providers that need them.</summary>
public sealed class EmailProviderSettings
{
    /// <summary>Provider name (<see cref="EmailProviders"/>, or a custom one). Empty means inherit.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string Provider { get; set; } = EmailProviders.Inherit;
    /// <summary>SMTP host. Not used by <c>google</c> (it is fixed).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? Host { get; set; }
    /// <summary>SMTP port. Default 587 (STARTTLS).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int Port { get; set; } = 587;
    /// <summary>Connection security. Not used by <c>google</c> (always STARTTLS).</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
    /// <summary>SMTP user name (for Google: the full Gmail address). Empty means no login.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? UserName { get; set; }
    /// <summary>SMTP password (for Google: an app password), encrypted. Never returned.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? PasswordProtected { get; set; }
    /// <summary>Address in the From header.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? FromAddress { get; set; }
    /// <summary>Display name in the From header. Empty: the brand name of the project.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? FromName { get; set; }
    /// <summary>Timeout of the whole send in seconds.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>How SMS messages are sent.</summary>
public sealed class SmsProviderSettings
{
    /// <summary>Provider name (<see cref="SmsProviders"/>, or a custom one). Empty means inherit.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string Provider { get; set; } = SmsProviders.Inherit;
    /// <summary>Sender id or number shown to the user, for the providers that support it.</summary>
    [SettingEditability(SettingEditor.GlobalOnly)] public string? Sender { get; set; }
}

/// <summary>
/// The sending channels of a project, or of one application (an application may use its own provider).
/// A level that leaves the provider empty uses the next one: application, then project, then the server default (<c>Auth2:Messaging</c>), then logging.
/// </summary>
public sealed class MessagingSettings
{
    /// <summary>Email provider.</summary>
    public EmailProviderSettings Email { get; set; } = new();
    /// <summary>SMS provider.</summary>
    public SmsProviderSettings Sms { get; set; } = new();
}
