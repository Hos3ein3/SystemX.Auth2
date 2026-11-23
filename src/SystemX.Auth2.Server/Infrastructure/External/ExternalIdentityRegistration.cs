using Microsoft.Extensions.DependencyInjection.Extensions;

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
        return services;
    }
}
