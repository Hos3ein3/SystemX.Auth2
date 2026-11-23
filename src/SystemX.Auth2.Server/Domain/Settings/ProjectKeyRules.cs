using System.Text.RegularExpressions;

namespace SystemX.Auth2.Server.Domain.Settings;

/// <summary>Rules for project keys: lowercase letters, digits, <c>-</c>, <c>_</c>; 3–64 chars; <c>_global</c> is reserved.</summary>
public static partial class ProjectKeyRules
{
    /// <summary>Key of the built-in global project.</summary>
    public const string GlobalProjectKey = "_global";

    [GeneratedRegex("^[a-z0-9_-]{3,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Pattern();

    /// <summary>True when the key has a valid shape (the reserved key is also valid in shape).</summary>
    public static bool IsValidShape(string? key) => key is not null && Pattern().IsMatch(key);

    /// <summary>True when the key may be used for a new, user-created application.</summary>
    public static bool IsValidForNewApplication(string? key) =>
        IsValidShape(key) && !string.Equals(key, GlobalProjectKey, StringComparison.Ordinal);
}
