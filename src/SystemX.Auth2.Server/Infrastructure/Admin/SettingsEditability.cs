using System.Collections;
using System.Reflection;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.Admin;

/// <summary>Finds which <c>GlobalOnly</c> settings a change touches, so a project admin cannot edit them.</summary>
public static class SettingsEditability
{
    /// <summary>Paths (like <c>Clients.AllowedScopes</c>) of <c>GlobalOnly</c> settings whose value differs between the two objects.</summary>
    public static IReadOnlyList<string> ChangedGlobalOnly(ApplicationSettings before, ApplicationSettings after)
    {
        var changed = new List<string>();
        Walk(before, after, "", changed);
        return changed;
    }

    private static void Walk(object a, object b, string prefix, List<string> changed)
    {
        foreach (var p in a.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = p.GetCustomAttribute<SettingEditabilityAttribute>();
            var path = prefix + p.Name;
            var x = p.GetValue(a);
            var y = p.GetValue(b);
            if (attr is null)
            {
                // A section: go inside. A value with no attribute is treated as GlobalOnly (safe default).
                if (x is not null && y is not null && IsSection(p.PropertyType)) Walk(x, y, path + ".", changed);
                else if (!Same(x, y)) changed.Add(path);
            }
            else if (attr.Editor == SettingEditor.GlobalOnly && !Same(x, y)) changed.Add(path);
        }
    }

    private static bool IsSection(Type t) => t.IsClass && t != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(t);

    private static bool Same(object? x, object? y)
    {
        if (x is null || y is null) return x is null && y is null;
        if (x is IEnumerable ex && x is not string) return y is IEnumerable ey && ex.Cast<object?>().Select(v => v?.ToString()).SequenceEqual(ey.Cast<object?>().Select(v => v?.ToString()));
        return Equals(x, y);
    }
}
