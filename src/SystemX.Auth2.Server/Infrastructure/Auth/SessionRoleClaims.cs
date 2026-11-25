using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// The session cookie only says who signed in and for which project. The server pages (the admin console, the home page) need to know the
/// roles, so they are looked up here in the database on each request (never stored in the cookie, so a removed role works at once).
/// GlobalAdmin and Developer count only in the <c>_global</c> project, the same rule as the admin API.
/// </summary>
internal sealed class SessionRoleClaims(IServiceScopeFactory scopes) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true, AuthenticationType: Auth2Cookie.Scheme } identity) return principal;
        if (identity.HasClaim(c => c.Type == "role" || c.Type == ClaimTypes.Role)) return principal;
        if (!Guid.TryParse(identity.FindFirst("sub")?.Value, out var id)) return principal;

        // A separate scope that works on the project of the cookie (this runs before the request has chosen a project).
        var key = identity.FindFirst(Auth2Claims.ProjectKey)?.Value;
        var isGlobalProject = string.Equals(key, Auth2Roles.GlobalProjectKey, StringComparison.Ordinal);
        IList<string> roles;
        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<SystemX.Auth2.Server.Application.IProjectSelection>().ProjectKey = key;
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(id.ToString("D"));
            if (user is null) return principal;
            roles = await users.GetRolesAsync(user);
        }
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim("role", role));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
            if (role is Auth2Roles.GlobalAdmin or Auth2Roles.Developer)
            {
                identity.AddClaim(new Claim("scope", "auth2.admin.global"));
            }
        }
        return principal;
    }
}
