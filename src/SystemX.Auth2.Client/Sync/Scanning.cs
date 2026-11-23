using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Sync;

/// <summary>The same key rules as the server: lowercase, 1–100 characters of <c>a-z 0-9 . _ : -</c>, starting and ending with a letter or digit.</summary>
public static class Auth2Keys
{
    /// <summary>Longest key.</summary>
    public const int MaxLength = 100;

    /// <summary>Trims and lower-cases. Returns null when the key is not valid.</summary>
    public static string? NormalizePermissionKey(string? key)
    {
        var k = key?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(k) || k.Length > MaxLength) return null;
        static bool Alnum(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';
        if (!Alnum(k[0]) || !Alnum(k[^1])) return null;
        return k.All(c => Alnum(c) || c is '.' or '_' or ':' or '-') ? k : null;
    }

    /// <summary>Role names: 1–100 letters, digits, space or <c>. _ : -</c>. Returns the trimmed name, or null.</summary>
    public static string? NormalizeRoleName(string? name)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n) || n.Length > MaxLength) return null;
        return n.All(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '_' or ':' or '-') ? n : null;
    }
}

/// <summary>Finds the permissions of this application: your list, and/or what your endpoints and attributes ask for.</summary>
public interface IPermissionScanner
{
    /// <summary>The permissions, sorted by key. Manual entries win over scanned ones (they have names and descriptions).</summary>
    IReadOnlyList<PermissionDefinition> Scan();
}

/// <summary>Finds the roles of this application.</summary>
public interface IRoleScanner
{
    /// <summary>The roles, sorted by name. Manual entries win over scanned ones.</summary>
    IReadOnlyList<RoleDefinition> Scan();
}

internal static class ScanSupport
{
    /// <summary>Metadata of every endpoint: Minimal API, controllers, Razor Pages (read after the application started).</summary>
    public static IEnumerable<object> EndpointMetadata(IEnumerable<EndpointDataSource> sources) => sources.SelectMany(s => s.Endpoints).SelectMany(e => e.Metadata);

    /// <summary>Attributes on the types and methods of the assemblies (Blazor components, services).</summary>
    public static IEnumerable<T> AttributesIn<T>(IEnumerable<Assembly> assemblies) where T : Attribute
    {
        foreach (var assembly in assemblies.Distinct())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = [.. ex.Types.Where(t => t is not null)!]; }
            foreach (var type in types)
            {
                foreach (var a in type.GetCustomAttributes<T>(inherit: true)) yield return a;
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    foreach (var a in method.GetCustomAttributes<T>(inherit: true)) yield return a;
            }
        }
    }
}

internal sealed class PermissionScanner(IEnumerable<EndpointDataSource> endpoints, IOptions<Auth2PermissionOptions> options, ILogger<PermissionScanner> log) : IPermissionScanner
{
    public IReadOnlyList<PermissionDefinition> Scan()
    {
        var o = options.Value;
        var result = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);

        void Add(PermissionDefinition d, bool manual)
        {
            var key = Auth2Keys.NormalizePermissionKey(d.Key);
            if (key is null) { log.LogWarning("The permission '{Key}' is not valid and is skipped. Use lowercase letters, digits and . _ : - (1 to 100 characters).", d.Key); return; }
            if (manual || !result.ContainsKey(key)) result[key] = d with { Key = key };   // manual wins, and the first scanned one stays
        }

        if (o.ScanningPolicy is ScanningPolicy.AssemblyScan or ScanningPolicy.Both)
        {
            foreach (var meta in ScanSupport.EndpointMetadata(endpoints).OfType<IAuth2PermissionMetadata>())
                foreach (var key in meta.Permissions) Add(new PermissionDefinition(key), manual: false);
            foreach (var attribute in ScanSupport.AttributesIn<Auth2PermissionAttribute>(o.AssembliesToScan))
                foreach (var key in attribute.Permissions) Add(new PermissionDefinition(key), manual: false);
        }
        if (o.ScanningPolicy is ScanningPolicy.Manual or ScanningPolicy.Both)
            foreach (var d in o.PermissionList) Add(d, manual: true);

        return [.. result.Values.OrderBy(d => d.Key, StringComparer.Ordinal)];
    }
}

internal sealed class RoleScanner(IEnumerable<EndpointDataSource> endpoints, IOptions<Auth2RoleOptions> options, ILogger<RoleScanner> log) : IRoleScanner
{
    public IReadOnlyList<RoleDefinition> Scan()
    {
        var o = options.Value;
        var result = new Dictionary<string, RoleDefinition>(StringComparer.OrdinalIgnoreCase);

        void Add(RoleDefinition d, bool manual)
        {
            var name = Auth2Keys.NormalizeRoleName(d.Name);
            if (name is null) { log.LogWarning("The role '{Name}' is not valid and is skipped.", d.Name); return; }
            var permissions = d.Permissions?.Select(Auth2Keys.NormalizePermissionKey).Where(k => k is not null).Select(k => k!).Distinct().Order(StringComparer.Ordinal).ToList();
            if (manual || !result.ContainsKey(name)) result[name] = d with { Name = name, Permissions = permissions };
        }

        if (o.ScanningPolicy is ScanningPolicy.AssemblyScan or ScanningPolicy.Both)
        {
            foreach (var meta in ScanSupport.EndpointMetadata(endpoints).OfType<IAuth2RoleMetadata>())
                foreach (var name in meta.Roles) Add(new RoleDefinition(name), manual: false);
            foreach (var attribute in ScanSupport.AttributesIn<Auth2RoleAttribute>(o.AssembliesToScan))
                foreach (var name in attribute.RoleNames) Add(new RoleDefinition(name), manual: false);
        }
        if (o.ScanningPolicy is ScanningPolicy.Manual or ScanningPolicy.Both)
            foreach (var d in o.RoleList) Add(d, manual: true);

        return [.. result.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
