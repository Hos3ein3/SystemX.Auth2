using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>
/// Creates the OpenIddict client for an application. Phase 3 only makes sure a basic client exists.
/// Redirect URIs, secrets and permissions are reconciled in phase 5.
/// </summary>
public sealed class OpenIddictClientProvisioner(IOpenIddictApplicationManager manager) : IClientProvisioner
{
    /// <inheritdoc />
    public async Task<Guid> EnsureClientAsync(Auth2Application application, CancellationToken ct = default)
    {
        var existing = await manager.FindByClientIdAsync(application.ClientId, ct);
        if (existing is not null)
            return Guid.Parse((await manager.GetIdAsync(existing, ct))!);

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = application.ClientId,
            DisplayName = application.Name,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        var created = await manager.CreateAsync(descriptor, ct);
        return Guid.Parse((await manager.GetIdAsync(created, ct))!);
    }

    /// <summary>All scope names the seeder creates.</summary>
    public static IReadOnlyList<string> StandardScopes { get; } =
    [
        Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Phone, Scopes.Roles, Scopes.OfflineAccess,
        .. Auth2Scopes.All,
    ];
}
