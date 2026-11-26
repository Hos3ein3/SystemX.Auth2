using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// Builds the claims of tokens. The token carries the roles of the current application and an access version (<c>pv</c>),
/// but not the permission list: clients ask <c>/api/auth2/me</c> and cache by <c>pv</c>.
/// <c>pv</c> is the application access version plus the user's access version in that application (both only grow).
/// Roles in the token are the effective roles: own roles plus roles of the user's groups.
/// </summary>
public sealed class TokenIssuer(
    IAccessService accessService, IAuthFlowResolver flow, IOpenIddictScopeManager scopeManager) : ITokenIssuer
{
    /// <summary>
    /// The audiences (<c>aud</c>) of an access token: the <b>client id of the application first</b>, then the resources of the granted scopes (the field
    /// "Associated Resources" of a scope), without duplicates. The client id stays first because the Server finds the application of an API call from the
    /// first audience, and the Client package accepts a token when any audience is the one it expects.
    /// </summary>
    internal async Task<IReadOnlyList<string>> AudiencesAsync(string clientId, IEnumerable<string> scopes, CancellationToken ct)
    {
        var audiences = new List<string> { clientId };
        var granted = scopes.Distinct(StringComparer.Ordinal).ToImmutableArray();
        if (granted.Length == 0) return audiences;
        await foreach (var resource in scopeManager.ListResourcesAsync(granted, ct))
            if (!string.IsNullOrWhiteSpace(resource) && !audiences.Contains(resource, StringComparer.Ordinal)) audiences.Add(resource);
        return audiences;
    }

    /// <inheritdoc />
    public async Task<ClaimsPrincipal> CreateUserPrincipalAsync(AppUser user, IEnumerable<string> scopes, IEnumerable<string> amr, string? sessionId, CancellationToken ct = default, string? identityProvider = null)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetEffectiveAsync(ct);
        var client = settings.Application ?? throw new ProjectNotFoundException("No application matches this request.");

        var identity = new ClaimsIdentity("Auth2", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString("D"));
        identity.SetClaim(Claims.PreferredUsername, user.UserName);
        identity.SetClaim(Claims.Email, user.Email);
        identity.SetClaim(Claims.EmailVerified, user.EmailConfirmed ? "true" : "false");
        identity.SetClaim(Claims.PhoneNumber, user.PhoneNumber);
        identity.SetClaim(Claims.PhoneNumberVerified, user.PhoneNumberConfirmed ? "true" : "false");
        identity.SetClaim(Claims.GivenName, user.FirstName);
        identity.SetClaim(Claims.FamilyName, user.LastName);
        identity.SetClaim(Claims.Name, FullName(user));
        identity.SetClaim(Auth2Claims.Locale, user.PreferredCulture);
        identity.SetClaim(Auth2Claims.ProjectId, app.Id.ToString("D"));
        identity.SetClaim(Auth2Claims.ProjectKey, app.ProjectKey);
        identity.SetClaim(Auth2Claims.ApplicationId, client.Id.ToString("D"));
        identity.SetClaim(Auth2Claims.ClientId, client.ClientId);
        identity.SetClaim(Auth2Claims.SessionId, sessionId);
        identity.SetClaim(Auth2Claims.IdentityProvider, identityProvider);
        foreach (var a in amr) identity.AddClaim(new Claim(Claims.AuthenticationMethodReference, a));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetResources(await AudiencesAsync(client.ClientId, scopes, ct));
        principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(settings.AccessTokenMinutes));
        principal.SetRefreshTokenLifetime(TimeSpan.FromDays(settings.RefreshTokenDays));
        principal.SetIdentityTokenLifetime(TimeSpan.FromMinutes(settings.IdentityTokenMinutes));

        await WriteAccessClaimsAsync(principal, user, app, ct);
        ApplyDestinations(principal);
        return principal;
    }

    /// <inheritdoc />
    public async Task<ClaimsPrincipal> CreateMachinePrincipalAsync(IEnumerable<string> scopes, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        var settings = await flow.GetEffectiveAsync(ct);
        var client = settings.Application ?? throw new ProjectNotFoundException("No application matches this request.");

        var identity = new ClaimsIdentity("Auth2", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, client.ClientId);
        identity.SetClaim(Auth2Claims.Machine, "true");
        identity.SetClaim(Auth2Claims.ProjectId, app.Id.ToString("D"));
        identity.SetClaim(Auth2Claims.ProjectKey, app.ProjectKey);
        identity.SetClaim(Auth2Claims.ApplicationId, client.Id.ToString("D"));
        identity.SetClaim(Auth2Claims.ClientId, client.ClientId);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetResources(await AudiencesAsync(client.ClientId, scopes, ct));
        principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(settings.AccessTokenMinutes));
        ApplyDestinations(principal);
        return principal;
    }

    /// <inheritdoc />
    public async Task RefreshUserClaimsAsync(ClaimsPrincipal principal, AppUser user, CancellationToken ct = default)
    {
        var app = await flow.GetProjectAsync(ct);
        await WriteAccessClaimsAsync(principal, user, app, ct);
        // A change of the resources of a scope reaches the token at its next refresh.
        if (principal.GetClaim(Auth2Claims.ClientId) is { Length: > 0 } clientId)
            principal.SetResources(await AudiencesAsync(clientId, principal.GetScopes(), ct));
        principal.SetClaim(Claims.Email, user.Email);
        principal.SetClaim(Claims.EmailVerified, user.EmailConfirmed ? "true" : "false");
        principal.SetClaim(Claims.PhoneNumber, user.PhoneNumber);
        principal.SetClaim(Claims.PhoneNumberVerified, user.PhoneNumberConfirmed ? "true" : "false");
        principal.SetClaim(Claims.Name, FullName(user));
        ApplyDestinations(principal);
    }

    private async Task WriteAccessClaimsAsync(ClaimsPrincipal principal, AppUser user, Project app, CancellationToken ct)
    {
        var access = await accessService.GetEffectiveAccessAsync(user, ct);
        principal.SetClaims(Claims.Role, [.. access.Roles]);
        principal.SetClaim(Auth2Claims.AccessVersion, access.AccessVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string? FullName(AppUser u)
    {
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : (full.Length > 0 ? full : u.UserName);
    }

    /// <summary>
    /// Every claim goes to the access token. Profile claims also go to the identity token when the scope was granted.
    /// </summary>
    internal static void ApplyDestinations(ClaimsPrincipal principal)
    {
        principal.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Name or Claims.PreferredUsername or Claims.GivenName or Claims.FamilyName or Auth2Claims.Locale
                when claim.Subject!.HasScope(Scopes.Profile) => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Email or Claims.EmailVerified when claim.Subject!.HasScope(Scopes.Email) => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.PhoneNumber or Claims.PhoneNumberVerified when claim.Subject!.HasScope(Scopes.Phone) => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Role when claim.Subject!.HasScope(Scopes.Roles) => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.AuthenticationMethodReference or Auth2Claims.IdentityProvider => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken],
        });
    }
}
