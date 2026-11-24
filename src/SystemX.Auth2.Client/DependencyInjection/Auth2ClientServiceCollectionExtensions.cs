using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client;
using Microsoft.AspNetCore.Authorization;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.DependencyInjection;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.HealthChecks;
using SystemX.Auth2.Client.Machine;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Client.Sync;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration of the SystemX.Auth2 client. Every method can be called in any order.</summary>
public static class Auth2ClientServiceCollectionExtensions
{
    /// <summary>Adds the client. <paramref name="serverUrl"/> is the address of the Auth2 Server.</summary>
    public static IAuth2ClientBuilder AddAuth2Client(this IServiceCollection services, string serverUrl, Action<Auth2ClientOptions>? configure = null)
    {
        services.AddOptions<Auth2ClientOptions>().Configure(o => o.ServerUrl = serverUrl);
        if (configure is not null) services.Configure(configure);
        return Register(services, null);
    }

    /// <summary>Adds the client with options from configuration (keys such as <c>ServerUrl</c>, <c>ApplicationName</c>, <c>ClientId</c>, <c>ClientSecret</c>, <c>ProjectKey</c>).</summary>
    public static IAuth2ClientBuilder AddAuth2Client(this IServiceCollection services, IConfigurationSection section)
    {
        services.AddOptions<Auth2ClientOptions>().Bind(section);
        return Register(services, section);
    }

    private static Auth2ClientBuilder Register(IServiceCollection services, IConfigurationSection? section)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<Auth2ClientOptions>, Auth2ClientOptionsValidator>());
        services.AddOptions<Auth2ClientOptions>().ValidateOnStart();
        services.AddOptions<Auth2MachineOptions>();
        services.AddOptions<Auth2PermissionOptions>();
        services.AddOptions<Auth2RoleOptions>();
        services.AddOptions<Auth2GroupOptions>();
        services.AddOptions<Auth2ScopeOptions>();
        services.AddOptions<Auth2CurrentUserOptions>();

        // Two named clients with the same address: one for calls that must never be repeated by themselves, one for calls that can be.
        foreach (var name in new[] { Auth2ClientDefaults.HttpClientName, Auth2ClientDefaults.ResilientHttpClientName })
            services.AddHttpClient(name).ConfigureHttpClient((sp, client) =>
            {
                var o = sp.GetRequiredService<IOptions<Auth2ClientOptions>>().Value;
                client.BaseAddress = new Uri(o.ServerUrl.TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan;   // our own timeouts apply (the resilience handler also has its own)
            });

        // Authorization and the current user
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddAuthorization();
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, Auth2PolicyProvider>());
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, Auth2AuthorizationResultHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, Auth2AuthorizationHandler>());
        services.TryAddSingleton<IAuth2AuditSink, LoggingAuditSink>();
        services.TryAddSingleton<ICurrentUserStore, MemoryCurrentUserStore>();
        services.TryAddSingleton<IAuth2AccessTokenAccessor, AccessTokenAccessor>();
        services.TryAddSingleton<IAuth2CurrentUserService, CurrentUserService>();
        services.TryAddScoped<IAuth2AccessProvider, AccessProvider>();
        services.TryAddScoped<ICurrentUser, CurrentUserImpl>();
        services.TryAddScoped<ICurrentClient, CurrentClientImpl>();

        // Scanners and sync (they do nothing while the policies are None)
        services.TryAddSingleton<IPermissionScanner, PermissionScanner>();
        services.TryAddSingleton<IRoleScanner, RoleScanner>();
        services.TryAddSingleton<IScopeScanner, ScopeScanner>();
        services.TryAddSingleton<IGroupScanner, GroupScanner>();
        services.TryAddSingleton<Auth2Catalog>();
        services.TryAddSingleton<IAuth2Catalog>(sp => sp.GetRequiredService<Auth2Catalog>());
        if (!services.Any(d => d.ServiceType == typeof(Auth2SyncHostedService)))
        {
            services.AddSingleton<Auth2SyncHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<Auth2SyncHostedService>());
        }

        services.TryAddSingleton<Auth2ClientState>();
        services.TryAddSingleton<IAuth2MachineTokenProvider, MachineTokenProvider>();
        services.TryAddSingleton<IAuth2MachineService, MachineService>();
        services.TryAddSingleton<HandshakeRunner>();
        services.TryAddSingleton<IAuth2ApiClientService, Auth2ApiClientService>();
        if (!services.Any(d => d.ServiceType == typeof(HandshakeHostedService)))
        {
            services.AddSingleton<HandshakeHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<HandshakeHostedService>());
        }
        return new Auth2ClientBuilder(services, section);
    }

    /// <summary>
    /// Says that the server is <b>multi-project</b> and what the project key of this application is.
    /// Leave this call out for a single-project server. The handshake checks that you chose right.
    /// </summary>
    public static IAuth2ClientBuilder ServerWithMultiProject(this IAuth2ClientBuilder builder, string projectKey)
    {
        builder.Services.Configure<Auth2ClientOptions>(o => o.ProjectKey = projectKey);
        return builder;
    }

    /// <summary>Sets how permissions are found and moved. The sync itself runs from a hosted service (see <c>permission-sync</c>).</summary>
    public static IAuth2ClientBuilder EnablePermissions(this IAuth2ClientBuilder builder, Action<Auth2PermissionOptions>? configure = null)
    {
        if (configure is not null) builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>Sets how roles are found and moved.</summary>
    public static IAuth2ClientBuilder EnableRoles(this IAuth2ClientBuilder builder, Action<Auth2RoleOptions>? configure = null)
    {
        if (configure is not null) builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>Sets how custom scopes (for example <c>reports.read</c>) are found and moved. The sync registers them on the server and allows them for this application.</summary>
    public static IAuth2ClientBuilder EnableScopes(this IAuth2ClientBuilder builder, Action<Auth2ScopeOptions>? configure = null)
    {
        if (configure is not null) builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>Sets how groups are found and moved (read, and with <c>Push</c> / <c>Both</c> also sent).</summary>
    public static IAuth2ClientBuilder EnableGroups(this IAuth2ClientBuilder builder, Action<Auth2GroupOptions>? configure = null)
    {
        if (configure is not null) builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>Turns on machine-to-machine tokens. Scopes you set here are used when a call names none.</summary>
    public static IAuth2ClientBuilder EnableMachineToMachine(this IAuth2ClientBuilder builder, Action<Auth2MachineOptions>? configure = null)
    {
        if (configure is not null) builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>Sets how the current user's access data is read and cached.</summary>
    public static IAuth2ClientBuilder EnableCurrentUser(this IAuth2ClientBuilder builder, Action<Auth2CurrentUserOptions>? configure = null)
    {
        if (configure is not null) builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>Keeps the access data of users in <c>IDistributedCache</c> (add Redis with <c>AddStackExchangeRedisCache</c>), so all instances of your application share it.</summary>
    public static IAuth2ClientBuilder UseDistributedCurrentUserStore(this IAuth2ClientBuilder builder)
    {
        builder.Services.AddDistributedMemoryCache();   // the default distributed cache, replaced when you add Redis
        builder.Services.Replace(ServiceDescriptor.Singleton<ICurrentUserStore, DistributedCurrentUserStore>());
        return builder;
    }

    /// <summary>
    /// Adds retry (with jitter), timeout and circuit breaker to the calls that can safely be repeated (token, handshake, sync, reads).
    /// Register, login and password calls are never retried.
    /// </summary>
    public static IAuth2ClientBuilder EnableResilience(this IAuth2ClientBuilder builder)
    {
        builder.Services.AddHttpClient(Auth2ClientDefaults.ResilientHttpClientName).AddStandardResilienceHandler();
        return builder;
    }

    /// <summary>Adds a health check named <c>auth2</c>: healthy when the last handshake worked, degraded otherwise.</summary>
    public static IAuth2ClientBuilder EnableHealthCheck(this IAuth2ClientBuilder builder)
    {
        builder.Services.AddHealthChecks().AddCheck<Auth2ClientHealthCheck>("auth2");
        builder.Services.TryAddSingleton<Auth2ClientHealthCheck>();
        return builder;
    }

    /// <summary>Makes an HTTP client send a machine token with every request. Name the scopes the called API needs.</summary>
    public static IHttpClientBuilder AddAuth2MachineToken(this IHttpClientBuilder builder, params string[] scopes) =>
        builder.AddHttpMessageHandler(sp => new MachineTokenHandler(sp.GetRequiredService<IAuth2MachineTokenProvider>(), scopes));
}

/// <summary>Middleware registration of the client.</summary>
public static class Auth2ClientApplicationBuilderExtensions
{
    /// <summary>Adds authentication and authorization to the pipeline, and the <c>/auth2/...</c> routes of the hosted UI. Call it where you would call <c>UseAuthentication()</c> and <c>UseAuthorization()</c>.</summary>
    public static IApplicationBuilder UseAuth2(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAuth2Routes();   // /auth2/login and /auth2/logout when a UI mode is on (minimal hosting)
        return app;
    }
}
