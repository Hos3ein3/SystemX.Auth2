using Microsoft.Extensions.DependencyInjection.Extensions;

using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.External;

internal static class ExternalIdentityRegistration
{
    public static IServiceCollection AddExternalIdentityCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IExternalSecretProtector, ExternalSecretProtector>();
        services.AddSingleton<IExternalProviderDescriptor, GoogleDescriptor>();
        services.AddSingleton<IExternalProviderDescriptor, MicrosoftDescriptor>();
        services.AddSingleton<IExternalProviderDescriptor, GitHubDescriptor>();
        services.AddSingleton<IExternalProviderDescriptor, AppleDescriptor>();
        services.AddSingleton<IExternalProviderDescriptor, FacebookDescriptor>();
        services.AddSingleton<IExternalProviderDescriptor, GenericOidcDescriptor>();
        services.AddSingleton<IExternalProviderDescriptor, SamlDescriptor>();
        services.AddSingleton<IExternalProviderDescriptor, LdapDescriptor>();
        services.TryAddSingleton<IExternalProviderDescriptors, ExternalProviderDescriptors>();
        services.AddHttpClient(ExternalHttp.ClientName, c => { c.Timeout = TimeSpan.FromSeconds(15); c.MaxResponseContentBufferSize = 1_000_000; })
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ConnectCallback = (ctx, ct) => SsrfGuard.ConnectAsync(ctx, ct, sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Application.Auth2ServerOptions>>().Value.ExternalProviders.AllowPrivateNetworks),
            });
        services.TryAddSingleton<IReplayCache, DatabaseReplayCache>();
        services.TryAddSingleton<IOidcEndpointResolver, OidcEndpointResolver>();
        services.TryAddSingleton<OidcIdentitySource>();
        services.AddSingleton<IExternalIdentitySource>(sp => sp.GetRequiredService<OidcIdentitySource>());
        services.TryAddSingleton<IExternalProviderRegistry, ExternalProviderRegistry>();
        services.AddHostedService<ExternalProviderRefreshService>();
        return services;
    }
}
