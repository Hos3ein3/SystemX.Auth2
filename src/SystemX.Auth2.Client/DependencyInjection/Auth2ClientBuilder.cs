using Microsoft.Extensions.Configuration;

namespace SystemX.Auth2.Client.DependencyInjection;

/// <summary>Returned by <c>AddAuth2Client</c>. The feature methods (<c>EnablePermissions</c>, <c>UseApiAuthentication</c>, ...) extend it. Order does not matter.</summary>
public interface IAuth2ClientBuilder
{
    /// <summary>The service collection.</summary>
    Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; }

    /// <summary>The configuration section the client was set up from, or null.</summary>
    IConfigurationSection? Configuration { get; }
}

internal sealed class Auth2ClientBuilder(Microsoft.Extensions.DependencyInjection.IServiceCollection services, IConfigurationSection? configuration) : IAuth2ClientBuilder
{
    public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; } = services;
    public IConfigurationSection? Configuration { get; } = configuration;
}
