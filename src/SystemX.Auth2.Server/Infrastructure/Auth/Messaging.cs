using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Resources;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// Development email sender. It only writes a log line with a masked recipient and the subject.
/// With <c>Auth2:Development:LogMessageBodies=true</c> and the Development environment it also logs the body (links and codes).
/// Replace it with a real sender in production by registering your own <see cref="IEmailSender"/>.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> log, IHostEnvironment env, IOptions<Auth2ServerOptions> options) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (env.IsDevelopment() && options.Value.Development.LogMessageBodies)
            log.LogWarning("DEVELOPMENT email to {To}: {Subject}\n{Body}", Mask.Email(message.To), message.Subject, message.TextBody);
        else
            log.LogInformation("Email to {To}: {Subject}", Mask.Email(message.To), message.Subject);
        return Task.CompletedTask;
    }
}

/// <summary>Development SMS sender. Same rules as <see cref="LoggingEmailSender"/>.</summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> log, IHostEnvironment env, IOptions<Auth2ServerOptions> options) : ISmsSender
{
    /// <inheritdoc />
    public Task SendAsync(SmsMessage message, CancellationToken ct = default)
    {
        if (env.IsDevelopment() && options.Value.Development.LogMessageBodies)
            log.LogWarning("DEVELOPMENT sms to {To}: {Text}", Mask.Phone(message.To), message.Text);
        else
            log.LogInformation("Sms to {To} (text not logged)", Mask.Phone(message.To));
        return Task.CompletedTask;
    }
}

/// <summary>Hides most of an address or number in logs.</summary>
public static class Mask
{
    /// <summary><c>sara@example.com</c> becomes <c>s***@example.com</c>.</summary>
    public static string Email(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : value[0] + "***" + value[at..];
    }

    /// <summary><c>+31612345678</c> becomes <c>+31*******78</c>.</summary>
    public static string Phone(string value) =>
        value.Length <= 5 ? "***" : value[..3] + new string('*', value.Length - 5) + value[^2..];
}

internal sealed class ServerUrls(IOptions<Auth2ServerOptions> options, IHttpContextAccessor http) : IServerUrls
{
    public string Build(string path, IEnumerable<KeyValuePair<string, string?>> query)
    {
        string baseUrl;
        if (!string.IsNullOrWhiteSpace(options.Value.Issuer)) baseUrl = options.Value.Issuer.TrimEnd('/');
        else if (http.HttpContext?.Request is { } r) baseUrl = $"{r.Scheme}://{r.Host}{r.PathBase}";
        else throw new InvalidOperationException("Set Auth2:Issuer: there is no request to take the server address from.");

        return QueryHelpers.AddQueryString(baseUrl + path, query.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => KeyValuePair.Create(p.Key, p.Value)));
    }
}

internal sealed class UserNotifier(
    IEmailSender email, ISmsSender sms, IServerUrls urls, IStringLocalizer<Emails> text, IAuthFlowResolver flow) : IUserNotifier
{
    public async Task SendEmailConfirmationAsync(AppUser user, string token, string? returnUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.Email)) return;
        var app = await flow.GetProjectAsync(ct);
        var link = urls.Build("/Account/ConfirmEmail", Query(user, (await flow.GetClientAsync(ct))?.ClientId, token, returnUrl));
        using var _ = Culture(user);
        await email.SendAsync(new EmailMessage(user.Email, text["confirm_email.subject"].Value,
            text["confirm_email.body", Brand(app), link].Value, new Dictionary<string, string> { ["token"] = token, ["link"] = link }), ct);
    }

    public async Task SendPasswordResetAsync(AppUser user, string token, string? returnUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.Email)) return;
        var app = await flow.GetProjectAsync(ct);
        var query = new List<KeyValuePair<string, string?>>
        {
            new("Login", user.Email), new("Token", token), new("ClientId", (await flow.GetClientAsync(ct))?.ClientId), new("ReturnUrl", returnUrl),
        };
        var link = urls.Build("/Account/ResetPassword", query);
        using var _ = Culture(user);
        await email.SendAsync(new EmailMessage(user.Email, text["reset_password.subject"].Value,
            text["reset_password.body", Brand(app), link].Value, new Dictionary<string, string> { ["token"] = token, ["link"] = link }), ct);
    }

    public async Task SendOtpAsync(AppUser user, string purpose, string code, NotificationChannel channel, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var data = new Dictionary<string, string> { ["code"] = code, ["purpose"] = purpose };
        using var _ = Culture(user);
        if (channel == NotificationChannel.Email && !string.IsNullOrEmpty(user.Email))
            await email.SendAsync(new EmailMessage(user.Email, text["otp.subject"].Value, text["otp.body", code, Brand(app)].Value, data), ct);
        else if (channel == NotificationChannel.Sms && !string.IsNullOrEmpty(user.PhoneNumber))
            await sms.SendAsync(new SmsMessage(user.PhoneNumber, text["sms.otp", code, Brand(app)].Value, data), ct);
    }

    private static string Brand(Project app) => app.Settings.Localization.BrandName ?? app.Name;

    private static List<KeyValuePair<string, string?>> Query(AppUser user, string? clientId, string token, string? returnUrl) =>
    [
        new("userId", user.Id.ToString("D")), new("token", token), new("ClientId", clientId), new("ReturnUrl", returnUrl),
    ];

    /// <summary>Writes the message in the user's saved language when the server has it.</summary>
    private static CultureScope Culture(AppUser user) => new(user.PreferredCulture);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _old = CultureInfo.CurrentUICulture;

        public CultureScope(string? culture)
        {
            var match = culture is null ? null : Localization.Auth2CultureResolver.Match(culture, Localization.Auth2CultureResolver.ServerCultures);
            if (match is not null) CultureInfo.CurrentUICulture = new CultureInfo(match);
        }

        public void Dispose() => CultureInfo.CurrentUICulture = _old;
    }
}
