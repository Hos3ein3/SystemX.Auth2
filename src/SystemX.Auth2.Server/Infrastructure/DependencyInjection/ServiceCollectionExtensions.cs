using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Infrastructure.Identity;
using SystemX.Auth2.Server.Infrastructure.Localization;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;
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
        services.AddScoped<IApplicationSettingsProvider, ApplicationSettingsProvider>();

        services.AddDbContext<Auth2DbContext>(o =>
        {
            o.UseNpgsql(connectionString);
            o.UseOpenIddict<Guid>();
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
                // Rules that depend on the project come from ApplicationSettings through IAuthFlowResolver.
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
        services.AddScoped<UserApplicationCredentialState>();
        services.AddScoped<ICredentialStateProvider, CredentialStateProvider>();
        services.AddScoped<IAuth2Localizer, Auth2Localizer>();

        services.AddSingleton<IReturnUrlValidator, ReturnUrlValidator>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITokenIssuer, TokenIssuer>();
        services.AddScoped<ILoginService, LoginService>();

        services.AddScoped<UserLookup>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IServerUrls, ServerUrls>();
        services.TryAddScoped<IEmailSender, LoggingEmailSender>();   // replace with your own sender in production
        services.TryAddScoped<ISmsSender, LoggingSmsSender>();
        services.AddScoped<IUserNotifier, UserNotifier>();
        services.AddScoped<IAccessVersionService, AccessVersionService>();
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
