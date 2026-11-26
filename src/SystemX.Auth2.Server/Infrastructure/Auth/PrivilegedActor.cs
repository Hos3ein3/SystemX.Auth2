using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>See <see cref="IPrivilegedActor"/>. Asked only when a built-in role is touched, so it is not cached.</summary>
internal sealed class PrivilegedActor(IHttpContextAccessor http, Auth2DbContext db) : IPrivilegedActor
{
    /// <inheritdoc />
    public async Task<bool> IsPrivilegedAsync(CancellationToken ct = default)
    {
        var context = http.HttpContext;
        if (context is null) return true;   // not a person: the seed, a job, code of the server itself

        var principal = context.User;
        var subject = principal.FindFirstValue(Auth2Claims.Subject) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(subject, out var userId)) return false;

        // The database decides, not the roles in the token or the cookie. Only the GLOBAL roles count (a project role with the same name is nothing).
        return await (from ur in db.UserRoles
                         join r in db.Roles on ur.RoleId equals r.Id
                         where ur.UserId == userId && r.ProjectId == null && (r.Name == Auth2Roles.GlobalAdmin || r.Name == Auth2Roles.Developer)
                         select ur.UserId).AnyAsync(ct);
    }
}
