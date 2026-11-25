using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.Caching;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Infrastructure.OpenIddict;

/// <summary>
/// The only code that creates, changes or deletes OpenIddict application rows. It always goes through
/// <see cref="IOpenIddictApplicationManager"/>, so OpenIddict validation and caching stay correct, and it reads the extra fields
/// (project, name, enabled flag, settings) from the same row. Every client is confidential (it has a secret).
/// </summary>
public sealed class OpenIddictClientProvisioner(
    IOpenIddictApplicationManager manager,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    Auth2DbContext db,
    IHybridCacheService cache) : IClientProvisioner
{
    /// <summary>Scopes the seeder creates (standard OIDC scopes and all <c>auth2.*</c> scopes).</summary>
    public static IReadOnlyList<string> StandardScopes { get; } =
    [
        Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Phone, Scopes.Roles, Scopes.OfflineAccess,
        .. Auth2Scopes.All,
    ];

    /// <inheritdoc />
    public async Task<ClientProvisionResult> CreateAsync(Project project, ApplicationSpec spec, DateTimeOffset now, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(spec.Name)) throw new ArgumentException("An application needs a name.", nameof(spec));
        var name = spec.Name.Trim();
        var clientId = string.IsNullOrWhiteSpace(spec.ClientId) ? $"{project.ProjectKey}.{name.ToLowerInvariant()}" : spec.ClientId.Trim();

        var entity = new Auth2Application
        {
            ProjectId = project.Id,
            Name = name,
            IsEnabled = true,
            ClientSettings = spec.Settings ?? new ClientApplicationSettings(),
            Kind = spec.ApplicationType ?? SystemX.Auth2.Contracts.Enums.ApplicationType.Other,
            WebServerUrl = WebServerUrls.Normalize(spec.WebServerUrl),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var secret = NewSecret();
        var descriptor = new OpenIddictApplicationDescriptor { ClientId = clientId };
        Apply(descriptor, entity.ClientSettings, project.Settings, name,
            spec.RedirectUris ?? [], spec.PostLogoutRedirectUris ?? [], spec.AllowedScopes ?? ClientScopeDefaults.DefaultScopes);
        await manager.PopulateAsync(entity, descriptor, ct);
        await manager.CreateAsync(entity, secret, ct);
        return new ClientProvisionResult(ToInfo(entity), secret);
    }

    /// <inheritdoc />
    public async Task<ClientApplicationInfo> UpdateAsync(Guid applicationId, ProjectSettings projectSettings, ApplicationSpec spec, bool? isEnabled, DateTimeOffset now, CancellationToken ct = default)
    {
        // Two updates of the same client at the same moment (parallel syncs, an admin change) can meet the OpenIddict concurrency token.
        // The row is read again and the change is applied again: the result is the same, because the descriptor is built from the stored state.
        for (var attempt = 0; ; attempt++)
        {
            try { return await UpdateOnceAsync(applicationId, projectSettings, spec, isEnabled, now, ct); }
            catch (OpenIddictExceptions.ConcurrencyException) when (attempt < 5) { await Task.Delay(20 * (attempt + 1), ct); }
        }
    }

    private async Task<ClientApplicationInfo> UpdateOnceAsync(Guid applicationId, ProjectSettings projectSettings, ApplicationSpec spec, bool? isEnabled, DateTimeOffset now, CancellationToken ct)
    {
        var entity = await FindEntityAsync(applicationId, ct) ?? throw new ProjectNotFoundException("Application not found.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, entity, ct);

        var settings = spec.Settings ?? entity.ClientSettings;
        var redirects = spec.RedirectUris ?? descriptor.RedirectUris.Select(u => u.OriginalString).ToList();
        var postLogout = spec.PostLogoutRedirectUris ?? descriptor.PostLogoutRedirectUris.Select(u => u.OriginalString).ToList();
        var scopes = spec.AllowedScopes ?? ScopesOf(descriptor.Permissions);
        var name = string.IsNullOrWhiteSpace(spec.Name) ? entity.Name : spec.Name.Trim();
        Apply(descriptor, settings, projectSettings, name, redirects, postLogout, scopes);
        await manager.PopulateAsync(entity, descriptor, ct);

        entity.Name = name;
        entity.ClientSettings = settings;
        if (spec.ApplicationType is { } type) entity.Kind = type;
        if (spec.WebServerUrl is { } url) entity.WebServerUrl = WebServerUrls.Normalize(url);   // "" clears it
        if (isEnabled is { } enabled) entity.IsEnabled = enabled;
        entity.SettingsVersion++;
        entity.UpdatedAtUtc = now;
        db.Entry(entity).Property(a => a.ClientSettings).IsModified = true;
        await manager.UpdateAsync(entity, ct);
        cache.InvalidateClient(entity.ClientId!);   // every instance reads the application again (the caller also does it after its own commit)
        return ToInfo(entity);
    }

    /// <inheritdoc />
    public async Task ResyncProjectAsync(Guid projectId, ProjectSettings projectSettings, DateTimeOffset now, CancellationToken ct = default)
    {
        var ids = await db.Set<Auth2Application>().AsNoTracking().Where(a => a.ProjectId == projectId).Select(a => a.Id).ToListAsync(ct);
        foreach (var id in ids) await UpdateAsync(id, projectSettings, new ApplicationSpec(""), null, now, ct);
    }

    /// <inheritdoc />
    public async Task<string> RotateSecretAsync(string clientId, CancellationToken ct = default)
    {
        var entity = await manager.FindByClientIdAsync(clientId, ct) as Auth2Application
            ?? throw new InvalidOperationException("The client does not exist. Run the seed command.");
        var secret = NewSecret();
        await manager.UpdateAsync(entity, secret, ct);
        return secret;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid applicationId, CancellationToken ct = default)
    {
        var entity = await FindEntityAsync(applicationId, ct);
        if (entity is null) return;
        await manager.DeleteAsync(entity, ct);
        cache.InvalidateClientCompletely(entity.ClientId!);
    }

    /// <inheritdoc />
    public async Task<int> RevokeAsync(Guid projectId, Guid? applicationId, CancellationToken ct = default)
    {
        var query = db.Set<Auth2Application>().AsNoTracking().Where(a => a.ProjectId == projectId);
        if (applicationId is { } one) query = query.Where(a => a.Id == one);
        var ids = await query.Select(a => a.Id).ToListAsync(ct);
        var count = 0;
        foreach (var id in ids)
        {
            var key = id.ToString("D");
            count += (int)await authorizations.RevokeByApplicationIdAsync(key, ct);
            count += (int)await tokens.RevokeByApplicationIdAsync(key, ct);
        }
        return count;
    }

    private async Task<Auth2Application?> FindEntityAsync(Guid applicationId, CancellationToken ct) =>
        await manager.FindByIdAsync(applicationId.ToString("D"), ct) as Auth2Application;

    private static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static List<string> ScopesOf(IEnumerable<string> permissions) =>
        permissions.Where(p => p.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal)).Select(p => p[Permissions.Prefixes.Scope.Length..]).ToList();

    /// <summary>Copies the values that OpenIddict stores into the descriptor. The extra fields are set by the caller.</summary>
    internal static void Apply(OpenIddictApplicationDescriptor d, ClientApplicationSettings app, ProjectSettings project, string name,
        IEnumerable<string> redirectUris, IEnumerable<string> postLogoutRedirectUris, IEnumerable<string> scopes)
    {
        d.DisplayName = name;
        d.ClientType = ClientTypes.Confidential;
        d.ConsentType = (app.ConsentMode ?? ConsentPolicy.Effective(project)) switch
        {
            ConsentMode.Explicit => ConsentTypes.Explicit,
            ConsentMode.Systematic => ConsentTypes.Systematic,
            _ => ConsentTypes.Implicit,
        };

        d.RedirectUris.Clear();
        foreach (var uri in redirectUris) d.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        d.PostLogoutRedirectUris.Clear();
        foreach (var uri in postLogoutRedirectUris) d.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));

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
        if (app.AllowSelfUiLogin)
        {
            d.Permissions.Add(Permissions.GrantTypes.Password);
            d.Permissions.Add(Permissions.Prefixes.GrantType + Auth2GrantTypes.TwoFactor);
            d.Permissions.Add(Permissions.Prefixes.GrantType + Auth2GrantTypes.Otp);
        }

        foreach (var scope in scopes.Distinct(StringComparer.Ordinal))
            d.Permissions.Add(Permissions.Prefixes.Scope + scope);

        d.Requirements.Clear();
        d.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
    }

    /// <summary>Maps the stored row to the DTO the rest of the server uses.</summary>
    internal static ClientApplicationInfo ToInfo(Auth2Application a) => new(
        a.Id, a.ProjectId, a.ClientId ?? "", a.Name, a.IsEnabled,
        ReadArray(a.RedirectUris), ReadArray(a.PostLogoutRedirectUris),
        ReadArray(a.Permissions).Where(p => p.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal)).Select(p => p[Permissions.Prefixes.Scope.Length..]).ToList(),
        a.ConsentType, a.ClientSettings, a.SettingsVersion, a.CreatedAtUtc, a.Kind, a.WebServerUrl);

    private static List<string> ReadArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
