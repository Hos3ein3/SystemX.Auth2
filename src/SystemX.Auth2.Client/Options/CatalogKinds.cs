using System.Reflection;
using Microsoft.Extensions.Configuration;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Contracts.Access;

namespace SystemX.Auth2.Client.Options;

public sealed partial class Auth2PermissionOptions
{
    /// <inheritdoc />
    protected override string Kind => "permission";
    /// <inheritdoc />
    protected override string KeyOf(PermissionDefinition d) => d.Key;
    /// <inheritdoc />
    protected override string? CheckKey(string key) => CatalogRules.CheckLowerKey(key);
    /// <inheritdoc />
    protected override bool SameData(PermissionDefinition a, PermissionDefinition b) => a == b;
    /// <inheritdoc />
    protected override PermissionDefinition FromKey(string key, string? category) => new(key, Group: category);
    /// <inheritdoc />
    protected override PermissionDefinition FromMember(string key, string defaultDisplayName, MemberInfo member, string? category)
    {
        var a = member.GetCustomAttributes<Auth2PermissionAttribute>().FirstOrDefault();
        return new PermissionDefinition(key, a?.DisplayName ?? defaultDisplayName, a?.Description, a?.Group ?? category);
    }
    /// <inheritdoc />
    protected override PermissionDefinition FromConfiguration(IConfigurationSection s) =>
        s.Value is { Length: > 0 } v ? new(v.Trim()) : new((s["Key"] ?? s["Name"] ?? s.Key).Trim(), s["DisplayName"], s["Description"], s["Group"]);
}

public sealed partial class Auth2RoleOptions
{
    /// <inheritdoc />
    protected override string Kind => "role";
    /// <inheritdoc />
    protected override string KeyOf(RoleDefinition d) => d.Name;
    /// <inheritdoc />
    protected override string? CheckKey(string key) => CatalogRules.CheckRoleName(key);
    /// <inheritdoc />
    protected override bool SameData(RoleDefinition a, RoleDefinition b) =>
        a.Name == b.Name && a.DisplayName == b.DisplayName && a.Description == b.Description
        && (a.Permissions ?? []).Order(StringComparer.Ordinal).SequenceEqual((b.Permissions ?? []).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    /// <inheritdoc />
    protected override RoleDefinition FromKey(string key, string? category) => new(key);
    /// <inheritdoc />
    protected override RoleDefinition FromMember(string key, string defaultDisplayName, MemberInfo member, string? category)
    {
        var a = member.GetCustomAttributes<Auth2RoleAttribute>().FirstOrDefault();
        return new RoleDefinition(key, a?.DisplayName ?? defaultDisplayName, a?.Description, a?.Permissions);
    }
    /// <inheritdoc />
    protected override RoleDefinition FromConfiguration(IConfigurationSection s) =>
        s.Value is { Length: > 0 } v ? new(v.Trim())
        : new((s["Name"] ?? s["Key"] ?? s.Key).Trim(), s["DisplayName"], s["Description"],
            s.GetSection("Permissions").GetChildren().Select(c => c.Value).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList());
}

public sealed partial class Auth2GroupOptions
{
    /// <inheritdoc />
    protected override string Kind => "group";
    /// <inheritdoc />
    protected override string KeyOf(GroupDefinition d) => d.Name;
    /// <inheritdoc />
    protected override string? CheckKey(string key) => CatalogRules.CheckLowerKey(key);
    /// <inheritdoc />
    protected override bool SameData(GroupDefinition a, GroupDefinition b) => a == b;
    /// <inheritdoc />
    protected override GroupDefinition FromKey(string key, string? category) => new(key);
    /// <inheritdoc />
    protected override GroupDefinition FromMember(string key, string defaultDisplayName, MemberInfo member, string? category) =>
        new(key, member.GetCustomAttributes<Auth2GroupAttribute>().FirstOrDefault()?.DisplayName ?? defaultDisplayName);
    /// <inheritdoc />
    protected override GroupDefinition FromConfiguration(IConfigurationSection s) =>
        s.Value is { Length: > 0 } v ? new(v.Trim()) : new((s["Name"] ?? s["Key"] ?? s.Key).Trim(), s["DisplayName"]);
}

public sealed partial class Auth2ScopeOptions
{
    /// <inheritdoc />
    protected override string Kind => "scope";
    /// <inheritdoc />
    protected override string KeyOf(ScopeDefinition d) => d.Name;
    /// <inheritdoc />
    protected override string? CheckKey(string key) => CatalogRules.CheckScopeName(key);
    /// <inheritdoc />
    protected override bool SameData(ScopeDefinition a, ScopeDefinition b) => a == b;
    /// <inheritdoc />
    protected override ScopeDefinition FromKey(string key, string? category) => new(key);
    /// <inheritdoc />
    protected override ScopeDefinition FromMember(string key, string defaultDisplayName, MemberInfo member, string? category)
    {
        var a = member.GetCustomAttributes<Auth2ScopeAttribute>().FirstOrDefault();
        return new ScopeDefinition(key, a?.DisplayName ?? defaultDisplayName, a?.Description);
    }
    /// <inheritdoc />
    protected override ScopeDefinition FromConfiguration(IConfigurationSection s) =>
        s.Value is { Length: > 0 } v ? new(v.Trim()) : new((s["Name"] ?? s["Key"] ?? s.Key).Trim(), s["DisplayName"], s["Description"]);
}
