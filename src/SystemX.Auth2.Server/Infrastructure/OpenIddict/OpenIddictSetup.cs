using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Infrastructure.OpenIddict;

/// <summary>OpenIddict server and validation setup.</summary>
public static class OpenIddictSetup
{
    /// <summary>Environments where temporary keys and plain HTTP are accepted.</summary>
    public static bool IsDevelopmentLike(IHostEnvironment env) => env.IsDevelopment() || env.IsEnvironment("Testing");

    /// <summary>Adds the OpenIddict server (passthrough endpoints) and validation.</summary>
    public static IServiceCollection AddAuth2OpenIddict(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Auth2KeyOptions>().Bind(configuration.GetSection(Auth2KeyOptions.SectionName));

        services.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<Auth2DbContext>().ReplaceDefaultEntities<Auth2Application, Auth2Authorization, Auth2Scope, Auth2Token, Guid>())
            .AddServer(o =>
            {
                o.SetAuthorizationEndpointUris(Auth2Routes.Authorize)
                 .SetTokenEndpointUris(Auth2Routes.Token)
                 .SetEndSessionEndpointUris(Auth2Routes.EndSession)
                 .SetUserInfoEndpointUris(Auth2Routes.UserInfo)
                 .SetIntrospectionEndpointUris(Auth2Routes.Introspect)
                 .SetRevocationEndpointUris(Auth2Routes.Revoke);

                // Authorization code + PKCE (required), client credentials and refresh token are the normal flows.
                // The password grant and the two-factor grant are registered here but each client needs the permission,
                // and the permission is only given when the application setting AllowSelfUiLogin is true.
                o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
                 .AllowClientCredentialsFlow()
                 .AllowRefreshTokenFlow()
                 .AllowPasswordFlow()
                 .AllowCustomFlow(Auth2GrantTypes.TwoFactor)
                 .AllowCustomFlow(Auth2GrantTypes.Otp);

                o.RegisterScopes(OpenIddictClientProvisioner.StandardScopes.ToArray());

                // Access tokens: signed JWT, not encrypted, so APIs can validate them with the public keys. Stored token
                // entries make revocation work. Code and refresh tokens stay encrypted. Refresh tokens rotate; reuse revokes the chain.
                o.DisableAccessTokenEncryption();
                o.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                o.SetRefreshTokenReuseLeeway(TimeSpan.Zero);

                o.UseAspNetCore()
                 .EnableAuthorizationEndpointPassthrough()
                 .EnableTokenEndpointPassthrough()
                 .EnableEndSessionEndpointPassthrough()
                 .EnableUserInfoEndpointPassthrough()
                 .EnableStatusCodePagesIntegration();

                // A token of a disabled project or application stops working (see ProjectStatusHandlers).
                o.AddEventHandler(ProjectStatusServerHandler.Descriptor);
            })
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseAspNetCore();
                o.EnableAuthorizationEntryValidation();
                o.EnableTokenEntryValidation();
                o.AddEventHandler(ProjectStatusValidationHandler.Descriptor);
            });

        // Keys, issuer and transport rules depend on the environment, so they are set when the options are built.
        services.AddOptions<OpenIddictServerOptions>().Configure<IHostEnvironment, IOptions<Auth2KeyOptions>, IOptions<Auth2ServerOptions>>((o, env, keys, server) =>
        {
            // Only S256. The "plain" method sends the verifier as the challenge, so it protects nothing.
            o.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain);

            if (!string.IsNullOrWhiteSpace(server.Value.Issuer)) o.Issuer = new Uri(server.Value.Issuer, UriKind.Absolute);

            if (keys.Value.HasCertificates)
            {
                foreach (var c in keys.Value.SigningCertificates)
                    o.SigningCredentials.Add(new SigningCredentials(new X509SecurityKey(Load(c)), SecurityAlgorithms.RsaSha256));
                foreach (var c in keys.Value.EncryptionCertificates)
                    o.EncryptionCredentials.Add(new EncryptingCredentials(new X509SecurityKey(Load(c)), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
            }
            // Otherwise the keys come from the key ring (database), see KeyRingOptionsConfigurator.
        });

        services.AddOptions<OpenIddictServerAspNetCoreOptions>().Configure<IHostEnvironment>((o, env) =>
        {
            if (IsDevelopmentLike(env)) o.DisableTransportSecurityRequirement = true;
        });
        return services;
    }

    private static X509Certificate2 Load(CertificateFileOptions c) =>
        X509CertificateLoader.LoadPkcs12FromFile(c.Path, c.Password, Operations.KeyRing.KeyStorageFlags);
}

/// <summary>Stops the application at startup when it is not safe to run in Production.</summary>
public static class ProductionGuard
{
    /// <summary>
    /// Outside Development and Testing, the server needs an HTTPS issuer. Keys are either configured certificates or the key ring in the database
    /// (temporary keys that vanish at restart do not exist any more).
    /// </summary>
    public static void Check(IHostEnvironment env, Auth2KeyOptions keys, Auth2ServerOptions server)
    {
        if (OpenIddictSetup.IsDevelopmentLike(env)) return;
        if (string.IsNullOrWhiteSpace(server.Issuer) || !server.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Auth2:Issuer must be set to an https:// URL outside Development.");
    }
}
