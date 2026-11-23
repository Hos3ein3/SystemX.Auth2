using Microsoft.AspNetCore.Authorization;

namespace SystemX.Auth2.Client.Authorization;

/// <summary>Endpoint metadata: the endpoint needs these permissions. Scanners read it (phase 13) to find the permissions of your application.</summary>
public interface IAuth2PermissionMetadata
{
    /// <summary>Permission keys.</summary>
    IReadOnlyList<string> Permissions { get; }

    /// <summary>True when all are needed, false when one is enough.</summary>
    bool RequireAll { get; }
}

/// <summary>Endpoint metadata: the endpoint needs these roles.</summary>
public interface IAuth2RoleMetadata
{
    /// <summary>Role names.</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>True when all are needed, false when one is enough.</summary>
    bool RequireAll { get; }
}

/// <summary>Endpoint metadata: the token needs these scopes.</summary>
public interface IAuth2ScopeMetadata
{
    /// <summary>Scope names (one is enough).</summary>
    IReadOnlyList<string> Scopes { get; }
}

/// <summary>Endpoint metadata: the user must be in one of these groups.</summary>
public interface IAuth2GroupMetadata
{
    /// <summary>Group names (one is enough).</summary>
    IReadOnlyList<string> Groups { get; }
}

/// <summary>Where a required permission must come from.</summary>
public enum Auth2PermissionSource
{
    /// <summary>Anywhere (direct grant, role, group). The default.</summary>
    Any = 0,
    /// <summary>From a role, or from the role of a group. A direct grant is not enough.</summary>
    RoleOnly = 1,
    /// <summary>From a direct grant to the user. A role is not enough.</summary>
    UserOnly = 2,
}

/// <summary>Endpoint metadata: where the permissions of this endpoint must come from.</summary>
public interface IAuth2PermissionSourceMetadata
{
    /// <summary>The source.</summary>
    Auth2PermissionSource Source { get; }
}

/// <summary>Endpoint metadata: only machine tokens (client credentials), or only user tokens.</summary>
public interface IAuth2TokenKindMetadata
{
    /// <summary>True for machine tokens only, false for user tokens only.</summary>
    bool MachineOnly { get; }
}

/// <summary>Endpoint metadata: name of an audit event to raise when the endpoint is authorized or refused.</summary>
public interface IAuth2AuditMetadata
{
    /// <summary>Event name, for example <c>orders.delete</c>.</summary>
    string Action { get; }
}

internal static class PolicyNames
{
    public const string Prefix = "auth2:";
    public const char Separator = '|';

    public static string Make(string kind, string mode, IEnumerable<string> values) => $"{Prefix}{kind}:{mode}:{string.Join(Separator, values)}";
}

/// <summary>The user must be signed in (a valid token). Use on controllers, Razor Page models and Blazor components.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2AuthorizeAttribute : AuthorizeAttribute;

/// <summary>Anyone may call this, even without a token.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2AnonymousAttribute : Attribute, IAllowAnonymous;

/// <summary>The user needs a permission (one of the keys, or all of them with <see cref="RequireAll"/>).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2PermissionAttribute : AuthorizeAttribute, IAuth2PermissionMetadata
{
    private readonly bool _all;

    /// <summary>Needs any of the permissions.</summary>
    public Auth2PermissionAttribute(params string[] permissions) : this(false, permissions) { }

    /// <summary>Needs any (or all) of the permissions.</summary>
    public Auth2PermissionAttribute(bool requireAll, params string[] permissions)
    {
        ArgumentOutOfRangeException.ThrowIfZero(permissions.Length);
        Permissions = [.. permissions];
        _all = requireAll;
        Policy = PolicyNames.Make("perm", requireAll ? "all" : "any", permissions);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Permissions { get; }

    /// <inheritdoc />
    public bool RequireAll => _all;
}

/// <summary>The user needs a role (one of the names, or all with <c>requireAll</c>).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2RoleAttribute : AuthorizeAttribute, IAuth2RoleMetadata
{
    private readonly bool _all;

    /// <summary>Needs any of the roles.</summary>
    public Auth2RoleAttribute(params string[] roles) : this(false, roles) { }

    /// <summary>Needs any (or all) of the roles.</summary>
    public Auth2RoleAttribute(bool requireAll, params string[] roles)
    {
        ArgumentOutOfRangeException.ThrowIfZero(roles.Length);
        RoleNames = [.. roles];
        _all = requireAll;
        Policy = PolicyNames.Make("role", requireAll ? "all" : "any", roles);
    }

    /// <summary>The role names.</summary>
    public IReadOnlyList<string> RoleNames { get; }

    IReadOnlyList<string> IAuth2RoleMetadata.Roles => RoleNames;

    /// <inheritdoc />
    public bool RequireAll => _all;
}

/// <summary>The token needs one of these scopes.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2ScopeAttribute : AuthorizeAttribute, IAuth2ScopeMetadata
{
    /// <summary>Needs any of the scopes.</summary>
    public Auth2ScopeAttribute(params string[] scopes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(scopes.Length);
        Scopes = [.. scopes];
        Policy = PolicyNames.Make("scope", "any", scopes);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Scopes { get; }
}

/// <summary>The user must be in one of these groups.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2GroupAttribute : AuthorizeAttribute, IAuth2GroupMetadata
{
    /// <summary>Needs any of the groups.</summary>
    public Auth2GroupAttribute(params string[] groups)
    {
        ArgumentOutOfRangeException.ThrowIfZero(groups.Length);
        Groups = [.. groups];
        Policy = PolicyNames.Make("group", "any", groups);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Groups { get; }
}

/// <summary>Only machine tokens (client credentials) are accepted.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2MachineOnlyAttribute : AuthorizeAttribute, IAuth2TokenKindMetadata
{
    /// <summary>Creates the attribute.</summary>
    public Auth2MachineOnlyAttribute() => Policy = PolicyNames.Make("kind", "machine", []);

    /// <inheritdoc />
    public bool MachineOnly => true;
}

/// <summary>Only user tokens are accepted (not machine tokens).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2UserOnlyAttribute : AuthorizeAttribute, IAuth2TokenKindMetadata
{
    /// <summary>Creates the attribute.</summary>
    public Auth2UserOnlyAttribute() => Policy = PolicyNames.Make("kind", "user", []);

    /// <inheritdoc />
    public bool MachineOnly => false;
}

/// <summary>The permissions of this endpoint must come from a role (or the role of a group), not from a direct grant.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2OnlyRolePermissionAttribute : Attribute, IAuth2PermissionSourceMetadata
{
    /// <inheritdoc />
    public Auth2PermissionSource Source => Auth2PermissionSource.RoleOnly;
}

/// <summary>The permissions of this endpoint must be direct grants to the user, not come from a role.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2OnlyUserPermissionAttribute : Attribute, IAuth2PermissionSourceMetadata
{
    /// <inheritdoc />
    public Auth2PermissionSource Source => Auth2PermissionSource.UserOnly;
}

/// <summary>Raises an audit event (see <c>IAuth2AuditSink</c>) when this endpoint is authorized or refused.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2AuditAttribute(string action) : Attribute, IAuth2AuditMetadata
{
    /// <inheritdoc />
    public string Action { get; } = action;
}
