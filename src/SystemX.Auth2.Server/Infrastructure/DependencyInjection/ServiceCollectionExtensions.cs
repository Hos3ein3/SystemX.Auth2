using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Server.Persistence.Seeding;
using SystemX.Auth2.Server.Persistence.Services;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration of the SystemX.Auth2 Server.</summary>
public static class Auth2ServerServiceCollectionExtensions
{
    /// <summary>Name of the connection string.</summary>
    public const string ConnectionStringName = "Auth2";

    /// <summary>
    /// Registers the Server services: database, Identity, OpenIddict core, application context, seeding.
    /// The server is multi-application unless you also call <see cref="ServerWithSingleApplication"/>.
    /// </summary>
    public static IServiceCollection AddSystemXAuth2Server(this IServiceCollection services, IConfiguration configuration,
        Action<Auth2ServerOptions>? configure = null)
    {
        services.AddOptions<Auth2ServerOptions>().Bind(configuration.GetSection(Auth2ServerOptions.SectionName));
        if (configure is not null) services.Configure(configure);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ApplicationScope>();
        services.AddScoped<IApplicationScope>(sp => sp.GetRequiredService<ApplicationScope>());
        services.AddScoped<IGlobalScope>(sp => sp.GetRequiredService<ApplicationScope>());
        services.AddScoped<IClientIdAccessor>(sp => sp.GetRequiredService<ApplicationScope>());
        services.AddScoped<IApplicationSelection>(sp => sp.GetRequiredService<ApplicationScope>());
        services.TryAddScoped<IApplicationContext, ModeApplicationContext>();
        services.AddSingleton<IApplicationSettingsProvider, ApplicationSettingsProvider>();

        services.AddDbContext<Auth2DbContext>(o =>
        {
            o.UseNpgsql(connectionString);
            o.UseOpenIddict<Guid>();
        });

        // Keys live in the database so every instance shares them (scale-out). The application name is fixed.
        services.AddDataProtection().SetApplicationName("SystemX.Auth2").PersistKeysToDbContext<Auth2DbContext>();

        services.AddIdentityCore<AppUser>(o =>
            {
                // Identity rules that depend on the project come from ApplicationSettings (phase 4). These are the global ones.
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<Auth2DbContext>()
            .AddDefaultTokenProviders();

        services.AddOpenIddict().AddCore(o => o
            .UseEntityFrameworkCore().UseDbContext<Auth2DbContext>().ReplaceDefaultEntities<Guid>());

        services.AddScoped<IClientProvisioner, OpenIddictClientProvisioner>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<Auth2Seeder>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }

    /// <summary>
    /// Switches the server to single-application mode. Server layer only: the Client has no such method.
    /// In this mode <c>ProjectKey</c> and <c>user_applications</c> are not used.
    /// </summary>
    public static IServiceCollection ServerWithSingleApplication(this IServiceCollection services)
    {
        services.Configure<Auth2ServerOptions>(o =>
        {
            o.Mode = ServerMode.SingleApplication;
            o.CredentialScope = CredentialScope.Shared;
        });
        services.Replace(ServiceDescriptor.Scoped<IApplicationContext, SingleApplicationContext>());
        return services;
    }
}
