using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Identity;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>What a login text looks like.</summary>
internal enum IdentifierKind
{
    UserName,
    Email,
    Phone,
}

/// <summary>Finds users by user name, email or phone number, and checks membership of an application.</summary>
internal sealed class UserLookup(UserManager<AppUser> users, Auth2DbContext db, IOptions<Auth2ServerOptions> options)
{
    /// <summary>An <c>@</c> means email. A phone-number pattern means phone. Everything else is a user name.</summary>
    public IdentifierKind KindOf(string login, ProjectSettings settings)
    {
        if (login.Contains('@')) return IdentifierKind.Email;
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(settings.Phone.RegexTimeoutMs, 1, 1000));
        return Auth2UserValidator.SafeMatch(settings.Phone.PhoneNumberRegex, login.Trim(), timeout) ? IdentifierKind.Phone : IdentifierKind.UserName;
    }

    /// <summary>
    /// Finds the user. The <b>user name is the identity</b>: it is always unique, and it can be an email address, a phone number or plain text, so the exact user name is tried
    /// first, whatever the text looks like. Only when nobody has that user name is the text taken for an email or a phone number and looked up in those columns.
    /// Two users with one phone number: no user (the login is refused).
    /// </summary>
    public async Task<AppUser?> FindAsync(string login, ProjectSettings settings, CancellationToken ct) => (await FindMatchAsync(login, settings, ct)).User;

    /// <summary>The person whose user name is exactly this text (any letter case), or null.</summary>
    public Task<AppUser?> FindByUserNameAsync(string login) => users.FindByNameAsync(login.Trim());

    /// <summary>The same as <see cref="FindAsync"/>, and what the text matched: the user name, the email or the phone number (null when nobody was found).</summary>
    public async Task<(AppUser? User, IdentifierKind? Matched)> FindMatchAsync(string login, ProjectSettings settings, CancellationToken ct)
    {
        login = login.Trim();
        if (await users.FindByNameAsync(login) is { } byName) return (byName, IdentifierKind.UserName);

        switch (KindOf(login, settings))
        {
            case IdentifierKind.Email:
                return await users.FindByEmailAsync(login) is { } byEmail ? (byEmail, IdentifierKind.Email) : (null, null);
            case IdentifierKind.Phone:
                var matches = await db.Users.Where(u => u.PhoneNumber == login).Take(2).ToListAsync(ct);
                return matches.Count == 1 ? (matches[0], IdentifierKind.Phone) : (null, null);
            default:
                return (null, null);
        }
    }

    /// <summary>True when the user is an active member of the application. In single mode everybody is.</summary>
    public async Task<bool> IsMemberAsync(AppUser user, Project app, CancellationToken ct)
    {
        if (options.Value.Mode == ServerMode.SingleProject) return true;
        return await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == app.Id && x.Status == UserProjectStatus.Active, ct);
    }

    /// <summary>True in single mode, or when a membership row exists (any status).</summary>
    public async Task<bool> HasRowOrSingleAsync(AppUser user, Project app, CancellationToken ct) =>
        options.Value.Mode == ServerMode.SingleProject || await HasRowAsync(user, app, ct);

    /// <summary>True when a membership row exists, whatever its status.</summary>
    public async Task<bool> HasRowAsync(AppUser user, Project app, CancellationToken ct)
    {
        if (options.Value.Mode == ServerMode.SingleProject) return true;
        return await db.UserProjects.AnyAsync(x => x.UserId == user.Id && x.ProjectId == app.Id, ct);
    }
}
