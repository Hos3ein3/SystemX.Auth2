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

    /// <summary>
    /// True for scopes that belong to the server (<c>auth2.*</c> and the standard OpenID Connect scopes). They are never sent by sync.
    /// </summary>
    public static bool IsReservedScope(string name) => name.StartsWith("auth2.", StringComparison.Ordinal) || name is "openid" or "profile" or "email" or "phone" or "roles" or "offline_access";

    /// <summary>Scope names follow the rules of permission keys. Returns null when the name is invalid.</summary>
    public static string? NormalizeScopeName(string? name) => NormalizePermissionKey(name);

    /// <summary>Group names follow the rules of permission keys (lowercase). Returns null when the name is invalid.</summary>
    public static string? NormalizeGroupName(string? name) => NormalizePermissionKey(name);

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

/// <summary>Finds the custom scopes of this application (for example <c>reports.read</c>). Reserved scopes are skipped.</summary>
public interface IScopeScanner
{
    /// <summary>The scopes, sorted by name. Manual entries win over scanned ones.</summary>
    IReadOnlyList<ScopeDefinition> Scan();
}

/// <summary>Finds the groups of this application (for <c>GroupSyncPolicy.Push</c> and <c>Both</c>).</summary>
public interface IGroupScanner
{
    /// <summary>The groups, sorted by name. Manual entries win over scanned ones.</summary>
    IReadOnlyList<GroupDefinition> Scan();
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
            // Manual wins. Of two scanned ones the first stays, unless it has no text and the later one has (the same key on two endpoints).
            if (manual || !result.TryGetValue(key, out var old) || (HasNoText(old) && !HasNoText(d))) result[key] = d with { Key = key };
        }

        static bool HasNoText(PermissionDefinition d) => d.DisplayName is null && d.Description is null && d.Group is null;

        // The texts of an attribute belong to its key when it names exactly one key; with several keys they would be ambiguous and are not used.
        static PermissionDefinition Of(string key, object meta) =>
            meta is Auth2PermissionAttribute { Permissions.Count: 1 } a ? new PermissionDefinition(key, a.DisplayName, a.Description, a.Group) : new PermissionDefinition(key);

        if (o.ScanningPolicy is ScanningPolicy.AssemblyScan or ScanningPolicy.Both)
        {
            foreach (var meta in ScanSupport.EndpointMetadata(endpoints).OfType<IAuth2PermissionMetadata>())
                foreach (var key in meta.Permissions) Add(Of(key, meta), manual: false);
            foreach (var attribute in ScanSupport.AttributesIn<Auth2PermissionAttribute>(o.AssembliesToScan))
                foreach (var key in attribute.Permissions) Add(Of(key, attribute), manual: false);
        }
        if (o.ScanningPolicy is ScanningPolicy.Manual or ScanningPolicy.Both)
        {
            foreach (var d in o.Merge()) Add(d, manual: true);   // AddFromClass, AddFromEnum, AddFromString, providers, ...
            foreach (var d in o.PermissionList) Add(d, manual: true);
        }

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
            if (manual || !result.TryGetValue(name, out var old) || (HasNoText(old) && !HasNoText(d))) result[name] = d with { Name = name, Permissions = permissions };
        }

        static bool HasNoText(RoleDefinition d) => d.DisplayName is null && d.Description is null && d.Permissions is not { Count: > 0 };

        static RoleDefinition Of(string name, object meta) =>
            meta is Auth2RoleAttribute { RoleNames.Count: 1 } a ? new RoleDefinition(name, a.DisplayName, a.Description, a.Permissions) : new RoleDefinition(name);

        if (o.ScanningPolicy is ScanningPolicy.AssemblyScan or ScanningPolicy.Both)
        {
            foreach (var meta in ScanSupport.EndpointMetadata(endpoints).OfType<IAuth2RoleMetadata>())
                foreach (var name in meta.Roles) Add(Of(name, meta), manual: false);
            foreach (var attribute in ScanSupport.AttributesIn<Auth2RoleAttribute>(o.AssembliesToScan))
                foreach (var name in attribute.RoleNames) Add(Of(name, attribute), manual: false);
        }
        if (o.ScanningPolicy is ScanningPolicy.Manual or ScanningPolicy.Both)
        {
            foreach (var d in o.Merge()) Add(d, manual: true);
            foreach (var d in o.RoleList) Add(d, manual: true);
        }

        return [.. result.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];
    }
}

internal sealed class ScopeScanner(IEnumerable<EndpointDataSource> endpoints, IOptions<Auth2ScopeOptions> options, ILogger<ScopeScanner> log) : IScopeScanner
{
    public IReadOnlyList<ScopeDefinition> Scan()
    {
        var o = options.Value;
        var result = new Dictionary<string, ScopeDefinition>(StringComparer.Ordinal);

        void Add(ScopeDefinition d, bool manual)
        {
            var name = Auth2Keys.NormalizeScopeName(d.Name);
            if (name is null) { log.LogWarning("The scope '{Name}' is not valid and is skipped. Use lowercase letters, digits and . _ : - (1 to 100 characters).", d.Name); return; }
            if (Auth2Keys.IsReservedScope(name))
            {
                if (manual) log.LogWarning("The scope '{Name}' is reserved (auth2.* and the standard OpenID Connect scopes) and is skipped.", name);
                return;   // endpoints often ask for auth2.me and similar: that is normal and silent
            }
            if (manual || !result.TryGetValue(name, out var old) || (HasNoText(old) && !HasNoText(d))) result[name] = d with { Name = name };
        }

        static bool HasNoText(ScopeDefinition d) => d.DisplayName is null && d.Description is null;

        static ScopeDefinition Of(string name, object meta) =>
            meta is Auth2ScopeAttribute { Scopes.Count: 1 } a ? new ScopeDefinition(name, a.DisplayName, a.Description) : new ScopeDefinition(name);

        if (o.ScanningPolicy is ScanningPolicy.AssemblyScan or ScanningPolicy.Both)
        {
            foreach (var meta in ScanSupport.EndpointMetadata(endpoints).OfType<IAuth2ScopeMetadata>())
                foreach (var name in meta.Scopes) Add(Of(name, meta), manual: false);
            foreach (var attribute in ScanSupport.AttributesIn<Auth2ScopeAttribute>(o.AssembliesToScan))
                foreach (var name in attribute.Scopes) Add(Of(name, attribute), manual: false);
        }
        if (o.ScanningPolicy is ScanningPolicy.Manual or ScanningPolicy.Both)
        {
            foreach (var d in o.Merge()) Add(d, manual: true);
            foreach (var d in o.ScopeList) Add(d, manual: true);
        }

        return [.. result.Values.OrderBy(d => d.Name, StringComparer.Ordinal)];
    }
}

internal sealed class GroupScanner(IEnumerable<EndpointDataSource> endpoints, IOptions<Auth2GroupOptions> options, ILogger<GroupScanner> log) : IGroupScanner
{
    public IReadOnlyList<GroupDefinition> Scan()
    {
        var o = options.Value;
        var result = new Dictionary<string, GroupDefinition>(StringComparer.Ordinal);

        void Add(GroupDefinition d, bool manual)
        {
            var name = Auth2Keys.NormalizeGroupName(d.Name);
            if (name is null) { log.LogWarning("The group '{Name}' is not valid and is skipped. Use lowercase letters, digits and . _ : - (1 to 100 characters).", d.Name); return; }
            if (manual || !result.TryGetValue(name, out var old) || (old.DisplayName is null && d.DisplayName is not null)) result[name] = d with { Name = name };
        }

        static GroupDefinition Of(string name, object meta) =>
            meta is Auth2GroupAttribute { Groups.Count: 1 } a ? new GroupDefinition(name, a.DisplayName) : new GroupDefinition(name);

        if (o.ScanningPolicy is ScanningPolicy.AssemblyScan or ScanningPolicy.Both)
        {
            foreach (var meta in ScanSupport.EndpointMetadata(endpoints).OfType<IAuth2GroupMetadata>())
                foreach (var name in meta.Groups) Add(Of(name, meta), manual: false);
            foreach (var attribute in ScanSupport.AttributesIn<Auth2GroupAttribute>(o.AssembliesToScan))
                foreach (var name in attribute.Groups) Add(Of(name, attribute), manual: false);
        }
        if (o.ScanningPolicy is ScanningPolicy.Manual or ScanningPolicy.Both)
        {
            foreach (var d in o.Merge()) Add(d, manual: true);
            foreach (var d in o.GroupList) Add(d, manual: true);
        }

        return [.. result.Values.OrderBy(d => d.Name, StringComparer.Ordinal)];
    }
}
