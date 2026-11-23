using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SystemX.Auth2.Client.Authorization;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Authorization for Minimal API endpoints (and route groups). Chaining means AND: <c>.WithRole("Admin").WithPermission("orders.write")</c> needs both.
/// Each call adds metadata, so the scanners can read what your endpoints need.
/// </summary>
public static class Auth2EndpointExtensions
{
    /// <summary>A signed-in user (valid token) is needed.</summary>
    public static TBuilder WithAuth2<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2AuthorizeAttribute());

    /// <summary>Anyone may call, even without a token.</summary>
    public static TBuilder WithAnonymous<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder => builder.AllowAnonymous();

    /// <summary>The user needs <b>any</b> of these roles (checked in the token).</summary>
    public static TBuilder WithRole<TBuilder>(this TBuilder builder, params string[] roles) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2RoleAttribute(false, roles));

    /// <summary>The user needs <b>all</b> of these roles.</summary>
    public static TBuilder WithAllRoles<TBuilder>(this TBuilder builder, params string[] roles) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2RoleAttribute(true, roles));

    /// <summary>The user needs <b>any</b> of these permissions.</summary>
    public static TBuilder WithPermission<TBuilder>(this TBuilder builder, params string[] permissions) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2PermissionAttribute(false, permissions));

    /// <summary>The user needs <b>all</b> of these permissions.</summary>
    public static TBuilder WithAllPermissions<TBuilder>(this TBuilder builder, params string[] permissions) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2PermissionAttribute(true, permissions));

    /// <summary>The permissions of this endpoint must come from a role (or the role of a group). A direct grant is not enough.</summary>
    public static TBuilder WithOnlyRolePermission<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new Auth2OnlyRolePermissionAttribute());

    /// <summary>The permissions of this endpoint must be direct grants to the user. A role is not enough.</summary>
    public static TBuilder WithOnlyUserPermission<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new Auth2OnlyUserPermissionAttribute());

    /// <summary>The token needs <b>any</b> of these scopes.</summary>
    public static TBuilder WithScope<TBuilder>(this TBuilder builder, params string[] scopes) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2ScopeAttribute(scopes));

    /// <summary>Only machine tokens (client credentials) are accepted.</summary>
    public static TBuilder WithMachineOnly<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2MachineOnlyAttribute());

    /// <summary>Only user tokens are accepted.</summary>
    public static TBuilder WithUserOnly<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2UserOnlyAttribute());

    /// <summary>The user must be in <b>one</b> of these groups.</summary>
    public static TBuilder WithGroup<TBuilder>(this TBuilder builder, params string[] groups) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2GroupAttribute(groups));

    /// <summary>Uses a policy you registered with <c>AddAuthorization</c>.</summary>
    public static TBuilder WithAuth2Policy<TBuilder>(this TBuilder builder, string policyName) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policyName);

    /// <summary>Raises an audit event (see <c>IAuth2AuditSink</c>) every time this endpoint is authorized or refused.</summary>
    public static TBuilder WithAuth2Audit<TBuilder>(this TBuilder builder, string action) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new Auth2AuditAttribute(action));
}
