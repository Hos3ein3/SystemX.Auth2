using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Infrastructure.External;
using SystemX.Auth2.Server.Infrastructure.Identity;
using SystemX.Auth2.Server.Infrastructure.Localization;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
using SystemX.Auth2.Server.Infrastructure.Operations;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using Microsoft.AspNetCore.HttpOverrides;
using SystemX.Auth2.Server.Persistence.Identity;
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
    /// The server is multi-project unless you also call <see cref="ServerWithSingleProject"/>.
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

        services.AddScoped<ProjectScope>();
        services.AddScoped<IProjectScope>(sp => sp.GetRequiredService<ProjectScope>());
        services.AddScoped<IGlobalScope>(sp => sp.GetRequiredService<ProjectScope>());
        services.AddScoped<IClientIdAccessor>(sp => sp.GetRequiredService<ProjectScope>());
        services.AddScoped<IProjectSelection>(sp => sp.GetRequiredService<ProjectScope>());
        services.AddScoped<IProjectCookieKey>(sp => sp.GetRequiredService<ProjectScope>());
        services.TryAddScoped<IProjectContext, ModeProjectContext>();
        services.AddScoped<IProjectSettingsProvider, ProjectSettingsProvider>();
        services.AddExternalIdentityCore();

        services.AddDbContext<Auth2DbContext>(o =>
        {
            o.UseNpgsql(connectionString);
            o.UseOpenIddict<Auth2Application, Auth2Authorization, Auth2Scope, Auth2Token, Guid>();
        });

        // Keys live in the database so every instance shares them (scale-out). The application name is fixed.
        services.AddDataProtection().SetApplicationName("SystemX.Auth2").PersistKeysToDbContext<Auth2DbContext>();

        services.AddLocalization();
        services.AddHttpContextAccessor();
        // The session cookie is the default way to know who is on a page (HttpContext.User), so every page, and its anti-forgery token,
        // sees the same user. API endpoints and /connect/userinfo name their own scheme (the OpenIddict validation or server scheme).
        services.AddAuthentication(o => o.DefaultAuthenticateScheme = Auth2Cookie.Scheme).AddAuth2Cookie();
        services.AddAuthorization(o => o.AddPolicy("Auth2.Session", p => p.AddAuthenticationSchemes(Auth2Cookie.Scheme).RequireAuthenticatedUser()));
        services.AddRazorPages(o => o.Conventions.AuthorizeFolder("/Manage", "Auth2.Session"));

        services.AddScoped<Auth2IdentityErrorDescriber>();
        services.AddIdentityCore<AppUser>(o =>
            {
                // Rules that depend on the project come from ProjectSettings through IAuthFlowResolver.
                // These are the global ones: email is unique, and the standard character and lockout rules are switched off
                // because Auth2UserValidator and Auth2UserManager apply the project rules.
                // Email uniqueness is checked by Auth2UserValidator (and the unique index), so a person may also have no email (phone only).
                o.User.RequireUniqueEmail = false;
                o.User.AllowedUserNameCharacters = "";
                o.Lockout.AllowedForNewUsers = false;
            })
            .AddRoles<AppRole>()
            .AddUserStore<Auth2UserStore>()
            .AddRoleStore<Auth2RoleStore>()
            .AddUserManager<Auth2UserManager>()
            .AddRoleManager<Auth2RoleManager>()
            .AddSignInManager<Auth2SignInManager>()
            .AddDefaultTokenProviders();
        // Passkeys: the options of Identity are the same for all applications. The per-application rules (user verification, limits)
        // are applied by PasskeyService. Only the domain and the allowed origins are set here.
        services.AddOptions<IdentityPasskeyOptions>().Configure<IOptions<Auth2ServerOptions>>((passkey, server) =>
        {
            var passkeys = server.Value;
            passkey.ServerDomain = string.IsNullOrWhiteSpace(passkeys.Passkeys.ServerDomain) ? null : passkeys.Passkeys.ServerDomain;
            passkey.UserVerificationRequirement = "preferred";
            passkey.ResidentKeyRequirement = "preferred";
            passkey.ValidateOrigin = context =>
            {
                if (context.CrossOrigin) return ValueTask.FromResult(false);
                var allowed = !string.IsNullOrWhiteSpace(passkeys.Issuer)
                    ? new Uri(passkeys.Issuer).GetLeftPart(UriPartial.Authority)
                    : $"{context.HttpContext.Request.Scheme}://{context.HttpContext.Request.Host}";
                return ValueTask.FromResult(string.Equals(context.Origin, allowed, StringComparison.OrdinalIgnoreCase));
            };
        });
        services.Replace(ServiceDescriptor.Scoped<IdentityErrorDescriber>(sp => sp.GetRequiredService<Auth2IdentityErrorDescriber>()));
        services.RemoveAll<IPasswordValidator<AppUser>>();
        services.AddScoped<IPasswordValidator<AppUser>, Auth2PasswordValidator>();
        services.AddScoped<IUserValidator<AppUser>, Auth2UserValidator>();
        services.RemoveAll<IRoleValidator<AppRole>>();
        services.AddScoped<IRoleValidator<AppRole>, Auth2RoleValidator>();
        services.TryAddScoped<IUserClaimsPrincipalFactory<AppUser>, UserClaimsPrincipalFactory<AppUser, AppRole>>();

        services.AddSingleton<ISecretHasher, Sha256SecretHasher>();
        services.AddScoped<IAuthFlowResolver, AuthFlowResolver>();
        services.AddScoped<UserRowCredentialState>();
        services.AddScoped<UserProjectCredentialState>();
        services.AddScoped<ICredentialStateProvider, CredentialStateProvider>();
        services.AddScoped<IAuth2Localizer, Auth2Localizer>();

        services.AddSingleton<IReturnUrlValidator, ReturnUrlValidator>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITokenIssuer, TokenIssuer>();
        services.AddScoped<LoginService>();
        services.AddScoped<ILoginService, LdapAwareLoginService>();

        services.AddScoped<UserLookup>();
        services.AddScoped<IOtpService, OtpService>();
        AddOperations(services, configuration);
        services.AddScoped<IServerUrls, ServerUrls>();
        services.TryAddScoped<IEmailSender, LoggingEmailSender>();   // replace with your own sender in production
        services.TryAddScoped<ISmsSender, LoggingSmsSender>();
        services.AddScoped<IUserNotifier, UserNotifier>();
        services.AddScoped<IAccessVersionService, AccessVersionService>();
        services.AddScoped<IAccessService, AccessService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IPermissionSyncService, PermissionSyncService>();
        services.AddScoped<IRoleSyncService, RoleSyncService>();
        services.AddScoped<IGroupSyncService, GroupSyncService>();
        services.AddScoped<IScopeSyncService, ScopeSyncService>();
        services.AddScoped<IClientHandshakeService, ClientHandshakeService>();
        services.AddScoped<IAdminUserService, SystemX.Auth2.Server.Infrastructure.Admin.AdminUserService>();
        services.AddScoped<IServerValues, SystemX.Auth2.Server.Infrastructure.Admin.ServerValues>();
        services.AddScoped<IAdminExternalService, SystemX.Auth2.Server.Infrastructure.Admin.AdminExternalService>();
        services.AddScoped<IAdminProjectService, SystemX.Auth2.Server.Infrastructure.Admin.AdminProjectService>();
        services.AddScoped<IAdminApplicationService, SystemX.Auth2.Server.Infrastructure.Admin.AdminApplicationService>();
        services.AddScoped<IAdminJobService, SystemX.Auth2.Server.Infrastructure.Admin.AdminJobService>();
        services.AddScoped<IAuditReader, SystemX.Auth2.Server.Infrastructure.Admin.AuditReader>();
        services.AddScoped<SystemX.Auth2.Server.Endpoints.Q>();
        services.AddScoped<IRegisterService, RegisterService>();
        services.AddScoped<IJoinService, JoinService>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<IConfirmationService, ConfirmationService>();
        services.AddScoped<IOtpRequestService, OtpRequestService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IConsentService, ConsentService>();

        services.AddAuth2OpenIddict(configuration);

        services.AddScoped<IClientProvisioner, OpenIddictClientProvisioner>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IApplicationService, SystemX.Auth2.Server.Persistence.Services.ApplicationService>();
        services.AddScoped<IClientApplicationContext, ClientApplicationContext>();
        services.AddSingleton<IProjectStatusCache, ProjectStatusCache>();
        services.AddScoped<Auth2Seeder>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }

    /// <summary>Rate limiting, Redis (optional), key ring, background jobs, health checks, CORS and hardening.</summary>
    private static void AddOperations(IServiceCollection services, IConfiguration configuration)
    {
        var redisOn = !string.IsNullOrWhiteSpace(configuration["Auth2:Redis:ConnectionString"]);
        if (redisOn)
        {
            services.AddSingleton<RedisConnection>();
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp => sp.GetRequiredService<RedisConnection>().Multiplexer);
            services.AddSingleton<IRateLimiter, RedisRateLimiter>();
            services.AddScoped<IOtpService, RedisOtpService>();   // replaces the table version registered before
            services.AddSingleton<ISettingsInvalidationBus, RedisSettingsInvalidationBus>();
            services.AddHostedService<SettingsInvalidationListener>();
        }
        else
        {
            services.AddSingleton<IRateLimiter, MemoryRateLimiter>();
            services.AddSingleton<ISettingsInvalidationBus, NoOpSettingsInvalidationBus>();
        }

        // The key ring in the database: used when no certificate files are configured.
        services.AddSingleton<KeyRing>();
        services.AddSingleton<IKeyRing>(sp => sp.GetRequiredService<KeyRing>());
        services.AddSingleton<IOptionsChangeTokenSource<OpenIddictServerOptions>>(sp => sp.GetRequiredService<KeyRing>());
        services.AddSingleton<IOptionsChangeTokenSource<global::OpenIddict.Validation.OpenIddictValidationOptions>, KeyRingValidationChangeSource>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, KeyRingOptionsConfigurator>();
        services.AddSingleton<IPostConfigureOptions<global::OpenIddict.Validation.OpenIddictValidationOptions>, KeyRingValidationConfigurator>();
        services.AddHostedService<KeyRingRefreshService>();

        services.AddSingleton<IDistributedLock, PostgresDistributedLock>();
        services.AddSingleton<MaintenanceJobs>();
        services.AddHostedService<MaintenanceService>();

        var health = services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        if (redisOn) health.AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

        services.AddCors();
        services.AddSingleton<Microsoft.AspNetCore.Cors.Infrastructure.ICorsPolicyProvider, Auth2CorsPolicyProvider>();
        HardeningSetup.ConfigureForwardedHeaders(services);
        services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));
    }

    /// <summary>
    /// Switches the server to single-project mode. Server layer only: the Client has no such method.
    /// In this mode <c>ProjectKey</c> and <c>user_applications</c> are not used.
    /// </summary>
    public static IServiceCollection ServerWithSingleProject(this IServiceCollection services)
    {
        services.Configure<Auth2ServerOptions>(o =>
        {
            o.Mode = ServerMode.SingleProject;
            o.CredentialScope = CredentialScope.Shared;
        });
        services.Replace(ServiceDescriptor.Scoped<IProjectContext, SingleProjectContext>());
        return services;
    }
}
