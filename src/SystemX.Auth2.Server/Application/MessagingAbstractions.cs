using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>An email provider setting with the password already decrypted. Lives for one send; never log it.</summary>
public sealed record EmailTransportConfig(string Provider, string? Host, int Port, SmtpSecurity Security, string? UserName, string? Password,
    string? FromAddress, string? FromName, int TimeoutSeconds);

/// <summary>An SMS provider setting.</summary>
public sealed record SmsTransportConfig(string Provider, string? Sender);

/// <summary>
/// One way to deliver email (SMTP, Google, a mail API ...). Register your own with <c>services.AddSingleton&lt;IEmailTransport, MyTransport&gt;()</c>;
/// an admin then selects it by <see cref="Name"/>.
/// </summary>
public interface IEmailTransport
{
    /// <summary>Provider name, lowercase (for example <c>smtp</c>).</summary>
    string Name { get; }
    /// <summary>Sends one email, or throws.</summary>
    Task SendAsync(EmailTransportConfig config, EmailMessage message, CancellationToken ct = default);
}

/// <summary>One way to deliver SMS. Register your own the same way as <see cref="IEmailTransport"/>.</summary>
public interface ISmsTransport
{
    /// <summary>Provider name, lowercase.</summary>
    string Name { get; }
    /// <summary>Sends one SMS, or throws.</summary>
    Task SendAsync(SmsTransportConfig config, SmsMessage message, CancellationToken ct = default);
}

/// <summary>
/// Gives the email sender for the current request. The application's provider wins, then the project's, then the server default (<c>Auth2:Messaging</c>), then logging.
/// </summary>
public interface IEmailFactory
{
    /// <summary>The names of the providers the server has.</summary>
    IReadOnlyCollection<string> ProviderNames { get; }
    /// <summary>The sender that fits the project and application of the current request.</summary>
    Task<IEmailSender> CreateAsync(CancellationToken ct = default);
}

/// <summary>Gives the SMS sender for the current request. Same rules as <see cref="IEmailFactory"/>.</summary>
public interface ISmsFactory
{
    /// <summary>The names of the providers the server has.</summary>
    IReadOnlyCollection<string> ProviderNames { get; }
    /// <summary>The sender that fits the project and application of the current request.</summary>
    Task<ISmsSender> CreateAsync(CancellationToken ct = default);
}
