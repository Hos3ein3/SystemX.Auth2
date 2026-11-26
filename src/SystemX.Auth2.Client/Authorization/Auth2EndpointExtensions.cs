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

    /// <summary>The user must be in <b>all</b> of these groups. Example: <c>.WithAllGroups("finance", "auditors")</c>.</summary>
    public static TBuilder WithAllGroups<TBuilder>(this TBuilder builder, params string[] groups) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2GroupAttribute(true, groups));

    /// <summary>
    /// The person must have signed in with <b>one</b> of these methods (the <c>amr</c> claim of the token): <c>pwd</c> password, <c>otp</c> one-time code, <c>swk</c> / <c>hwk</c> passkey, <c>mfa</c>.
    /// See <see cref="Auth2SignInMethods"/>. A machine token is refused. Example: <c>.WithSignInMethod(Auth2SignInMethods.SyncedPasskey, Auth2SignInMethods.DevicePasskey)</c>.
    /// </summary>
    public static TBuilder WithSignInMethod<TBuilder>(this TBuilder builder, params string[] methods) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2SignInMethodAttribute(methods));

    /// <summary>
    /// The request must carry a <b>fresh code of the authenticator app</b> (TOTP), checked on the Server every time, in the header <c>X-Auth2-Totp</c> (or the form field <c>auth2_totp</c>).
    /// For actions that need the person to be there right now. A code works once. The code is <b>required</b>: a person who has not turned on two-factor is refused with the error
    /// <c>totp_setup_required</c>, without a code the error is <c>totp_required</c>, with a wrong one <c>invalid_otp</c>. All are 403 with the header <c>X-Auth2-Totp-Status</c>
    /// (and a JSON body for API calls), see <see cref="Auth2Totp"/>. A machine token is refused. Example: <c>app.MapDelete("/api/account", Delete).WithTotp();</c>
    /// </summary>
    public static TBuilder WithTotp<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2TotpAttribute());

    /// <summary>
    /// Like <see cref="WithTotp{TBuilder}"/>, but only for people who <b>have</b> two-factor on: they must give a code. A person who has not turned it on passes (there is nothing to check).
    /// Use it where two-factor is a choice, not a rule. Example: <c>app.MapPost("/api/export", Export).WithOptionalTotp();</c>
    /// </summary>
    public static TBuilder WithOptionalTotp<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2OptionalTotpAttribute());

    /// <summary>The person must have used more than one factor to sign in (a verified passkey, or a password plus a one-time code). For endpoints that move money or change security settings.</summary>
    public static TBuilder WithMultiFactor<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new Auth2MultiFactorAttribute());

    /// <summary>
    /// "One of these is enough", across kinds: the user has any of <paramref name="roles"/>, or any of <paramref name="permissions"/>, or is in any of <paramref name="groups"/>,
    /// or the token has any of <paramref name="scopes"/>. Leave out the lists you do not need. At least one value is required. Scanned like the single-kind rules, so the roles,
    /// permissions, groups and scopes are synced.
    /// </summary>
    public static TBuilder WithAnyOf<TBuilder>(this TBuilder builder, string[]? roles = null, string[]? permissions = null, string[]? groups = null, string[]? scopes = null)
        where TBuilder : IEndpointConventionBuilder
    {
        if ((roles?.Length ?? 0) + (permissions?.Length ?? 0) + (groups?.Length ?? 0) + (scopes?.Length ?? 0) == 0)
            throw new ArgumentException("WithAnyOf needs at least one role, permission, group or scope.");
        return builder.RequireAuthorization(new Auth2AnyOfAttribute { Roles = roles ?? [], Permissions = permissions ?? [], Groups = groups ?? [], Scopes = scopes ?? [] });
    }

    /// <summary>The user has <b>any</b> of the roles <b>or any</b> of the permissions. Example: <c>.WithRoleOrPermission(["Admin"], ["orders.write"])</c>.</summary>
    public static TBuilder WithRoleOrPermission<TBuilder>(this TBuilder builder, string[] roles, string[] permissions) where TBuilder : IEndpointConventionBuilder =>
        builder.WithAnyOf(roles: roles, permissions: permissions);

    /// <summary>The user has <b>any</b> of the roles <b>or</b> is in <b>any</b> of the groups.</summary>
    public static TBuilder WithRoleOrGroup<TBuilder>(this TBuilder builder, string[] roles, string[] groups) where TBuilder : IEndpointConventionBuilder =>
        builder.WithAnyOf(roles: roles, groups: groups);

    /// <summary>The user has <b>any</b> of the permissions <b>or</b> is in <b>any</b> of the groups.</summary>
    public static TBuilder WithPermissionOrGroup<TBuilder>(this TBuilder builder, string[] permissions, string[] groups) where TBuilder : IEndpointConventionBuilder =>
        builder.WithAnyOf(permissions: permissions, groups: groups);

    /// <summary>The token has <b>any</b> of the scopes <b>or</b> the user has <b>any</b> of the roles. Example: a program with the scope, or an admin.</summary>
    public static TBuilder WithScopeOrRole<TBuilder>(this TBuilder builder, string[] scopes, string[] roles) where TBuilder : IEndpointConventionBuilder =>
        builder.WithAnyOf(roles: roles, scopes: scopes);

    /// <summary>Uses a policy you registered with <c>AddAuthorization</c>.</summary>
    public static TBuilder WithAuth2Policy<TBuilder>(this TBuilder builder, string policyName) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policyName);

    /// <summary>Raises an audit event (see <c>IAuth2AuditSink</c>) every time this endpoint is authorized or refused.</summary>
    public static TBuilder WithAuth2Audit<TBuilder>(this TBuilder builder, string action) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new Auth2AuditAttribute(action));
}
