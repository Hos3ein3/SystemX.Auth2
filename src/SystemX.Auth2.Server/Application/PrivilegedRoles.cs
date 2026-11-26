using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Server.Application;

/// <summary>The two built-in roles that give power over the whole server: <c>GlobalAdmin</c> and <c>Developer</c>.</summary>
public static class PrivilegedRoles
{
    /// <summary>True for <c>GlobalAdmin</c> and <c>Developer</c> (any letter case, spaces around are ignored).</summary>
    public static bool IsPrivileged(string? roleName)
    {
        var name = roleName?.Trim();
        return string.Equals(name, Auth2Roles.GlobalAdmin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Auth2Roles.Developer, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The person who makes the current change. The roles <see cref="PrivilegedRoles"/> can be given to somebody (a user, or a group, or by joining a group that has them)
/// or taken away only by a <c>Developer</c> or a <c>GlobalAdmin</c>. These two roles can never be deleted, and no role with their names can be created.
/// </summary>
public interface IPrivilegedActor
{
    /// <summary>
    /// True when the person who makes this change holds the global role <c>GlobalAdmin</c> or <c>Developer</c> (read from the database, not from the token).
    /// A change that is not made by a person (no web request: the seed, the background jobs) counts as trusted. A request without a signed-in person is not.
    /// </summary>
    Task<bool> IsPrivilegedAsync(CancellationToken ct = default);
}
