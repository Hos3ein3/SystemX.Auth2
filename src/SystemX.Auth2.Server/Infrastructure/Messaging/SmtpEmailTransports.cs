using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Infrastructure.Messaging;

/// <summary>Sends plain text email through any SMTP server (MailKit). The body and the password are never logged.</summary>
internal sealed class SmtpEmailTransport(ILogger<SmtpEmailTransport> log) : IEmailTransport
{
    public string Name => EmailProviders.Smtp;

    public Task SendAsync(EmailTransportConfig config, EmailMessage message, CancellationToken ct = default) => SendCoreAsync(config, message, ct);

    internal async Task SendCoreAsync(EmailTransportConfig config, EmailMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Host)) throw new InvalidOperationException("The SMTP host is not set.");
        if (string.IsNullOrWhiteSpace(config.FromAddress)) throw new InvalidOperationException("The From address is not set.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(config.FromName ?? "", config.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        var option = config.Security switch
        {
            SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurity.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 120)));
        try
        {
            using var client = new SmtpClient { Timeout = Math.Clamp(config.TimeoutSeconds, 1, 120) * 1000 };
            await client.ConnectAsync(config.Host, config.Port, option, timeout.Token);
            if (!string.IsNullOrEmpty(config.UserName)) await client.AuthenticateAsync(config.UserName, config.Password ?? "", timeout.Token);
            await client.SendAsync(mime, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The text of the exception can hold the server answer; log the type and the target only.
            log.LogError("Sending an email to {To} through {Host}:{Port} failed ({Error}).", Mask.Email(message.To), config.Host, config.Port, ex.GetType().Name);
            throw;
        }
    }
}

/// <summary>Gmail / Google Workspace: <c>smtp.gmail.com:587</c> with STARTTLS. The user name is the Gmail address and the password is an app password (2-step verification must be on).</summary>
internal sealed class GoogleEmailTransport(SmtpEmailTransport smtp) : IEmailTransport
{
    public const string Host = "smtp.gmail.com";
    public const int Port = 587;

    public string Name => EmailProviders.Google;

    public Task SendAsync(EmailTransportConfig config, EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(config.UserName) || string.IsNullOrEmpty(config.Password))
            throw new InvalidOperationException("The Google provider needs the Gmail address as user name and an app password.");
        return smtp.SendCoreAsync(config with { Host = Host, Port = Port, Security = SmtpSecurity.StartTls, FromAddress = string.IsNullOrWhiteSpace(config.FromAddress) ? config.UserName : config.FromAddress }, message, ct);
    }
}
