using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SystemX.Auth2.Server.Persistence;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Infrastructure.Identity;

/// <summary>Checks a password against the rules of the current application.</summary>
public sealed class Auth2PasswordValidator(IAuthFlowResolver flow, Auth2IdentityErrorDescriber describer, IPasswordHistoryService history) : IPasswordValidator<AppUser>
{
    /// <inheritdoc />
    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        var rules = (await flow.GetSettingsAsync()).Password;
        var errors = Check(rules, password ?? "", describer);
        // The history check hashes the password against old hashes, so it only runs when the plain rules passed.
        if (errors.Count == 0 && await history.IsReusedAsync(user, password ?? "")) errors.Add(describer.PasswordReused());
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    /// <summary>The pure rule check. Public for tests.</summary>
    public static List<IdentityError> Check(Domain.Settings.PasswordSettings rules, string password, Auth2IdentityErrorDescriber describer)
    {
        var errors = new List<IdentityError>();
        if (password.Length < rules.MinLength) errors.Add(describer.PasswordTooShort(rules.MinLength));
        if (password.Length > rules.MaxLength) errors.Add(describer.PasswordTooLong(rules.MaxLength));
        if (rules.RequireDigit && !password.Any(char.IsDigit)) errors.Add(describer.PasswordRequiresDigit());
        if (rules.RequireLower && !password.Any(char.IsLower)) errors.Add(describer.PasswordRequiresLower());
        if (rules.RequireUpper && !password.Any(char.IsUpper)) errors.Add(describer.PasswordRequiresUpper());
        if (rules.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit)) errors.Add(describer.PasswordRequiresNonAlphanumeric());
        if (rules.UniqueChars > 0 && password.Distinct().Count() < rules.UniqueChars) errors.Add(describer.PasswordRequiresUniqueChars(rules.UniqueChars));
        if (rules.BannedPasswords.Contains(password, StringComparer.OrdinalIgnoreCase)) errors.Add(describer.PasswordBanned());
        return errors;
    }
}

/// <summary>
/// Checks user name, email and phone number against the rules of the current application.
/// Uniqueness is handled by the standard Identity validator and the database indexes (global).
/// </summary>
public sealed class Auth2UserValidator(IAuthFlowResolver flow, Auth2IdentityErrorDescriber describer, Auth2DbContext db) : IUserValidator<AppUser>
{
    /// <inheritdoc />
    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
    {
        var s = await flow.GetSettingsAsync();
        var errors = new List<IdentityError>();

        var name = user.UserName ?? "";
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(s.Phone.RegexTimeoutMs, 1, 1000));
        if (name.Length < s.UserName.MinLength || name.Length > s.UserName.MaxLength || !SafeMatch(s.UserName.AllowedCharacters, name, timeout))
            errors.Add(describer.InvalidUserName(name));

        if (!string.IsNullOrEmpty(user.Email))
        {
            // The format is checked by the standard validator. Here: one email, one person (on the whole server), and the domain rules.
            var owner = await manager.FindByEmailAsync(user.Email);
            if (owner is not null && owner.Id != user.Id) errors.Add(describer.DuplicateEmail(user.Email));

            var at = user.Email.LastIndexOf('@');
            if (at > 0 && at < user.Email.Length - 1)
            {
                var domain = user.Email[(at + 1)..];
                var allowed = s.Email.AllowedDomains;
                if (s.Email.BlockedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase)
                    || (allowed.Count > 0 && !allowed.Contains(domain, StringComparer.OrdinalIgnoreCase)))
                    errors.Add(describer.EmailDomainNotAllowed(domain));
            }
        }

        if (!string.IsNullOrEmpty(user.PhoneNumber) && !SafeMatch(s.Phone.PhoneNumberRegex, user.PhoneNumber, timeout))
            errors.Add(describer.InvalidPhoneNumber());

        // The user name is the identity and is unique. It can be an email address or a phone number, so a person's email or phone number may not be the user name of ANOTHER person,
        // and a user name may not be the email or phone number of another person: a sign-in with that text would match two people.
        var id = user.Id;
        if (!string.IsNullOrEmpty(user.Email) && await db.Users.AnyAsync(u => u.Id != id && u.NormalizedUserName == manager.NormalizeName(user.Email)))
            errors.Add(describer.DuplicateEmail(user.Email));
        if (!string.IsNullOrEmpty(user.PhoneNumber) && await db.Users.AnyAsync(u => u.Id != id && u.NormalizedUserName == manager.NormalizeName(user.PhoneNumber)))
            errors.Add(describer.DuplicatePhoneNumber(user.PhoneNumber));
        if (name.Length > 0)
        {
            var normalizedEmail = manager.NormalizeEmail(name);
            if (await db.Users.AnyAsync(u => u.Id != id && (u.NormalizedEmail == normalizedEmail || u.PhoneNumber == name)))
                errors.Add(describer.DuplicateUserName(name));
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    /// <summary>Regex match with a timeout. A timeout counts as "no match" (never hang the server).</summary>
    public static bool SafeMatch(string pattern, string input, TimeSpan timeout)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.CultureInvariant, timeout); }
        catch (RegexMatchTimeoutException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
