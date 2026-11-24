using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Infrastructure.Messaging;

internal sealed class EmailFactory(IAuthFlowResolver flow, IEnumerable<IEmailTransport> transports, IOptions<Auth2ServerOptions> options,
    IExternalSecretProtector secrets) : IEmailFactory
{
    private readonly Dictionary<string, IEmailTransport> _transports = transports.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> ProviderNames => _transports.Keys;

    public async Task<IEmailSender> CreateAsync(CancellationToken ct = default)
    {
        var effective = await flow.GetEffectiveAsync(ct);
        var project = await flow.GetProjectAsync(ct);
        var brand = effective.Project.Localization.BrandName ?? project.Name;

        var config = effective.EmailProvider is { } p
            ? new EmailTransportConfig(p.Provider.Trim().ToLowerInvariant(), p.Host, p.Port, p.Security, p.UserName, secrets.Unprotect(p.PasswordProtected), p.FromAddress, p.FromName, p.TimeoutSeconds)
            : FromServer(options.Value.Messaging.Email);
        config = config with { FromName = string.IsNullOrWhiteSpace(config.FromName) ? brand : config.FromName };

        if (!_transports.TryGetValue(config.Provider, out var transport))
            throw new InvalidOperationException($"The email provider '{config.Provider}' is not registered. Known providers: {string.Join(", ", _transports.Keys.Order())}.");
        return new BoundEmailSender(transport, config);
    }

    private static EmailTransportConfig FromServer(ServerEmailOptions o) =>
        new(string.IsNullOrWhiteSpace(o.Provider) ? EmailProviders.Logging : o.Provider.Trim().ToLowerInvariant(), o.Host, o.Port, o.Security, o.UserName, o.Password, o.FromAddress, o.FromName, o.TimeoutSeconds);

    private sealed class BoundEmailSender(IEmailTransport transport, EmailTransportConfig config) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default) => transport.SendAsync(config, message, ct);
    }
}

internal sealed class SmsFactory(IAuthFlowResolver flow, IEnumerable<ISmsTransport> transports, IOptions<Auth2ServerOptions> options) : ISmsFactory
{
    private readonly Dictionary<string, ISmsTransport> _transports = transports.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> ProviderNames => _transports.Keys;

    public async Task<ISmsSender> CreateAsync(CancellationToken ct = default)
    {
        var effective = await flow.GetEffectiveAsync(ct);
        var server = options.Value.Messaging;
        var config = effective.SmsProvider is { } p
            ? new SmsTransportConfig(p.Provider.Trim().ToLowerInvariant(), p.Sender)
            : new SmsTransportConfig(string.IsNullOrWhiteSpace(server.SmsProvider) ? SmsProviders.Logging : server.SmsProvider.Trim().ToLowerInvariant(), server.SmsSender);
        if (!_transports.TryGetValue(config.Provider, out var transport))
            throw new InvalidOperationException($"The SMS provider '{config.Provider}' is not registered. Known providers: {string.Join(", ", _transports.Keys.Order())}.");
        return new BoundSmsSender(transport, config);
    }

    private sealed class BoundSmsSender(ISmsTransport transport, SmsTransportConfig config) : ISmsSender
    {
        public Task SendAsync(SmsMessage message, CancellationToken ct = default) => transport.SendAsync(config, message, ct);
    }
}

/// <summary>The default <see cref="IEmailSender"/>: asks the factory for the provider of the current project and application, then sends.</summary>
internal sealed class FactoryEmailSender(IEmailFactory factory) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default) => await (await factory.CreateAsync(ct)).SendAsync(message, ct);
}

/// <summary>The default <see cref="ISmsSender"/>.</summary>
internal sealed class FactorySmsSender(ISmsFactory factory) : ISmsSender
{
    public async Task SendAsync(SmsMessage message, CancellationToken ct = default) => await (await factory.CreateAsync(ct)).SendAsync(message, ct);
}

internal sealed class LoggingEmailTransport(LoggingEmailSender inner) : IEmailTransport
{
    public string Name => EmailProviders.Logging;
    public Task SendAsync(EmailTransportConfig config, EmailMessage message, CancellationToken ct = default) => inner.SendAsync(message, ct);
}

internal sealed class LoggingSmsTransport(LoggingSmsSender inner) : ISmsTransport
{
    public string Name => SmsProviders.Logging;
    public Task SendAsync(SmsTransportConfig config, SmsMessage message, CancellationToken ct = default) => inner.SendAsync(message, ct);
}
