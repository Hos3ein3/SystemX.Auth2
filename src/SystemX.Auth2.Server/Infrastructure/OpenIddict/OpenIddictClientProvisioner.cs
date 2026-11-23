using System.Security.Cryptography;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Infrastructure.OpenIddict;

/// <summary>
/// Keeps the OpenIddict client of an application in line with the application settings.
/// Every client is confidential (it has a secret), so the password grant is never open to public clients.
/// </summary>
public sealed class OpenIddictClientProvisioner(IOpenIddictApplicationManager manager) : IClientProvisioner
{
    /// <summary>Scopes the seeder creates (standard OIDC scopes and all <c>auth2.*</c> scopes).</summary>
    public static IReadOnlyList<string> StandardScopes { get; } =
    [
        Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Phone, Scopes.Roles, Scopes.OfflineAccess,
        .. Auth2Scopes.All,
    ];

    /// <inheritdoc />
    public async Task<ClientProvisionResult> EnsureClientAsync(Auth2Application application, CancellationToken ct = default)
    {
        var existing = await manager.FindByClientIdAsync(application.ClientId, ct);
        if (existing is null)
        {
            var secret = NewSecret();
            var descriptor = new OpenIddictApplicationDescriptor { ClientSecret = secret };
            Apply(descriptor, application);
            var created = await manager.CreateAsync(descriptor, ct);
            return new ClientProvisionResult(Guid.Parse((await manager.GetIdAsync(created, ct))!), secret);
        }

        // Copy the stored values (including the already hashed secret) into a descriptor, change what the settings control, save.
        var current = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(current, existing, ct);
        Apply(current, application);
        await manager.UpdateAsync(existing, current, ct);
        return new ClientProvisionResult(Guid.Parse((await manager.GetIdAsync(existing, ct))!), null);
    }

    /// <inheritdoc />
    public async Task<string> RotateSecretAsync(Auth2Application application, CancellationToken ct = default)
    {
        var existing = await manager.FindByClientIdAsync(application.ClientId, ct)
            ?? throw new InvalidOperationException("The OpenIddict client does not exist. Run the seed command.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, existing, ct);
        var secret = NewSecret();
        descriptor.ClientSecret = secret;
        await manager.UpdateAsync(existing, descriptor, ct);
        return secret;
    }

    private static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Copies the settings of the application into the client descriptor.</summary>
    internal static void Apply(OpenIddictApplicationDescriptor d, Auth2Application app)
    {
        var s = app.Settings;
        d.ClientId = app.ClientId;
        d.DisplayName = app.Name;
        d.ClientType = ClientTypes.Confidential;
        d.ConsentType = ConsentPolicy.Effective(s) switch
        {
            ConsentMode.Explicit => ConsentTypes.Explicit,
            ConsentMode.Systematic => ConsentTypes.Systematic,
            _ => ConsentTypes.Implicit,
        };

        d.RedirectUris.Clear();
        foreach (var uri in s.Redirects.AllowedRedirectUris) d.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        d.PostLogoutRedirectUris.Clear();
        foreach (var uri in s.Redirects.AllowedPostLogoutRedirectUris) d.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));

        d.Permissions.Clear();
        foreach (var p in new[]
        {
            Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession,
            Permissions.Endpoints.Revocation, Permissions.Endpoints.Introspection,
            Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.GrantTypes.ClientCredentials,
            Permissions.ResponseTypes.Code,
        })
            d.Permissions.Add(p);

        // The password grant and the two-factor grant exist only for applications that opted in (AllowSelfUiLogin).
        if (s.Clients.AllowSelfUiLogin)
        {
            d.Permissions.Add(Permissions.GrantTypes.Password);
            d.Permissions.Add(Permissions.Prefixes.GrantType + Auth2GrantTypes.TwoFactor);
            d.Permissions.Add(Permissions.Prefixes.GrantType + Auth2GrantTypes.Otp);
        }

        foreach (var scope in s.Clients.AllowedScopes.Distinct(StringComparer.Ordinal))
            d.Permissions.Add(Permissions.Prefixes.Scope + scope);

        d.Requirements.Clear();
        d.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
    }
}
