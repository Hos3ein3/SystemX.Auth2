using System.Security.Claims;
using OpenIddict.Abstractions;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// Finds the application of a token. A token has the client id of its application as the <b>first</b> audience, followed by the resources of its scopes
/// (see <see cref="TokenIssuer"/>). The application is taken from the claim <c>client_id</c> when that is one of the audiences, otherwise from the first audience,
/// so an extra audience (even the client id of another application, used as a resource name) never selects another application.
/// </summary>
public static class TokenAudience
{
    /// <summary>The client id of the application the token was made for, or null.</summary>
    public static string? ApplicationOf(ClaimsPrincipal principal)
    {
        var audiences = principal.GetAudiences();
        var fromClaim = principal.GetClaim(Auth2Claims.ClientId);
        return !string.IsNullOrEmpty(fromClaim) && audiences.Contains(fromClaim, StringComparer.Ordinal) ? fromClaim : audiences.FirstOrDefault();
    }
}
