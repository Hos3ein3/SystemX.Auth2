using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Application.Lists;

/// <summary>A user together with the membership in the project of the list (the membership is null in single-project mode).</summary>
public sealed class UserListEntity
{
    /// <summary>The person.</summary>
    public required AppUser User { get; init; }
    /// <summary>The membership row, or null.</summary>
    public UserProject? Membership { get; init; }
}

/// <summary>One row of the user list. No hash, key or code is in it.</summary>
public sealed record UserListRow(Guid Id, string? Email, string? UserName, string? Name, string Status, bool EmailConfirmed, bool TwoFactorEnabled,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? LastLoginUtc);

/// <summary>The list definition of users: whitelists for sort, filters and search. Index plan: (project_id, user_id) of user_projects, normalized email and user name of users.</summary>
public static class UserList
{
    /// <summary>The users of one project. Every list starts from this query, so it is always under the project (tenant) filter.</summary>
    public static IQueryable<UserListEntity> For(Auth2DbContext db, Guid projectId, bool singleProject) =>
        from u in db.Users.AsNoTracking()
        from m in db.UserProjects.IgnoreQueryFilters().AsNoTracking().Where(x => x.UserId == u.Id && x.ProjectId == projectId).DefaultIfEmpty()
        where singleProject || m != null
        select new UserListEntity { User = u, Membership = m };

    /// <summary>The definition.</summary>
    public static ListDefinition<UserListEntity, UserListRow> Definition { get; } = Build();

    private static ListDefinition<UserListEntity, UserListRow> Build()
    {
        static DateTimeOffset? ParseDate(string? s) => DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
        static bool? ParseBool(string? s) => bool.TryParse(s, out var b) ? b : null;

        return new ListDefinition<UserListEntity, UserListRow>
        {
            Sortable = new Dictionary<string, Expression<Func<UserListEntity, object?>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = e => e.User.Email,
                ["userName"] = e => e.User.UserName,
                ["name"] = e => e.User.DisplayName ?? (e.User.FirstName + " " + e.User.LastName),
                ["createdAt"] = e => e.User.CreatedAtUtc,
                ["lastLoginAt"] = e => e.Membership!.LastLoginUtc,
                ["status"] = e => e.User.IsActive,
            },
            Filters = new Dictionary<string, FilterRule<UserListEntity>>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = new() { Build = v => v.Select(x => x.ToLowerInvariant()).ToArray() switch
                {
                    ["active"] => e => e.User.IsActive,
                    ["disabled"] => e => !e.User.IsActive,
                    ["locked"] => e => e.User.IsActive && (e.Membership != null ? e.Membership.LockoutEnd : e.User.LockoutEnd) > DateTimeOffset.UtcNow,
                    _ => null,
                } },
                ["emailConfirmed"] = new() { Build = v => ParseBool(v[0]) is { } b ? e => e.User.EmailConfirmed == b : null },
                ["twoFactorEnabled"] = new() { Build = v => ParseBool(v[0]) is { } b ? e => (e.Membership != null ? e.Membership.TwoFactorEnabled : e.User.TwoFactorEnabled) == b : null },
                ["createdAt"] = new()
                {
                    Build = _ => null,
                    BuildRange = (from, to) =>
                    {
                        var f = from is null ? (DateTimeOffset?)null : ParseDate(from); var t = to is null ? (DateTimeOffset?)null : ParseDate(to);
                        if ((from is not null && f is null) || (to is not null && t is null)) return null;
                        return e => (f == null || e.User.CreatedAtUtc >= f) && (t == null || e.User.CreatedAtUtc <= t);
                    },
                },
                ["lastLoginAt"] = new()
                {
                    Build = _ => null,
                    BuildRange = (from, to) =>
                    {
                        var f = from is null ? (DateTimeOffset?)null : ParseDate(from); var t = to is null ? (DateTimeOffset?)null : ParseDate(to);
                        if ((from is not null && f is null) || (to is not null && t is null)) return null;
                        return e => (f == null || e.Membership!.LastLoginUtc >= f) && (t == null || e.Membership!.LastLoginUtc <= t);
                    },
                },
            },
            // text is already escaped for LIKE; prefix match (the normalized columns have an index)
            Search = text => e => EF.Functions.ILike(e.User.Email!, text + "%", ListQueryParser.EscapeChar)
                                  || EF.Functions.ILike(e.User.UserName!, text + "%", ListQueryParser.EscapeChar)
                                  || EF.Functions.ILike(e.User.FirstName!, text + "%", ListQueryParser.EscapeChar)
                                  || EF.Functions.ILike(e.User.LastName!, text + "%", ListQueryParser.EscapeChar)
                                  || EF.Functions.ILike(e.User.PhoneNumber!, text + "%", ListQueryParser.EscapeChar),
            DefaultSort = "-createdAt",
            Tiebreaker = e => e.User.Id,
            Select = e => new UserListRow(e.User.Id, e.User.Email, e.User.UserName, e.User.DisplayName ?? (e.User.FirstName + " " + e.User.LastName).Trim(),
                !e.User.IsActive ? "disabled" : ((e.Membership != null ? e.Membership.LockoutEnd : e.User.LockoutEnd) > DateTimeOffset.UtcNow ? "locked" : "active"),
                e.User.EmailConfirmed, e.Membership != null ? e.Membership.TwoFactorEnabled : e.User.TwoFactorEnabled, e.User.CreatedAtUtc, e.Membership == null ? null : e.Membership.LastLoginUtc),
        };
    }
}
