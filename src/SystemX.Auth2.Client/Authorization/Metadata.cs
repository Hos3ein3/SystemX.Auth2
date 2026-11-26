using Microsoft.AspNetCore.Http;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Contracts.Constants;
using Microsoft.AspNetCore.Authorization;

namespace SystemX.Auth2.Client.Authorization;

/// <summary>Endpoint metadata: the endpoint needs these permissions. Scanners read it to find the permissions of your application.</summary>
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

/// <summary>Endpoint metadata: the user must be in these groups.</summary>
public interface IAuth2GroupMetadata
{
    /// <summary>Group names.</summary>
    IReadOnlyList<string> Groups { get; }

    /// <summary>True when the user must be in all of the groups, false (the default) when one is enough.</summary>
    bool RequireAll => false;
}

/// <summary>Endpoint metadata: how the person must have signed in (the <c>amr</c> claim of the token).</summary>
public interface IAuth2SignInMethodMetadata
{
    /// <summary>The accepted <c>amr</c> values (one is enough), for example <c>pwd</c>, <c>otp</c>, <c>swk</c> (a synced passkey), <c>hwk</c> (a device-bound passkey), <c>mfa</c>. Empty when <see cref="MultiFactor"/> is used.</summary>
    IReadOnlyList<string> Methods { get; }

    /// <summary>True when the person must have used more than one factor (see <see cref="Auth2SignInMethods.IsMultiFactor"/>).</summary>
    bool MultiFactor { get; }
}

/// <summary>The <c>amr</c> values the Server writes into a token, and what counts as more than one factor.</summary>
public static class Auth2SignInMethods
{
    /// <summary>A password.</summary>
    public const string Password = "pwd";
    /// <summary>A one-time code (email, SMS or an authenticator app).</summary>
    public const string OneTimeCode = "otp";
    /// <summary>A passkey that syncs between devices (iCloud Keychain, Google Password Manager).</summary>
    public const string SyncedPasskey = "swk";
    /// <summary>A passkey bound to one device or a security key.</summary>
    public const string DevicePasskey = "hwk";
    /// <summary>More than one factor (a passkey that was unlocked with a fingerprint, face or PIN).</summary>
    public const string MultiFactor = "mfa";

    /// <summary>True when the person used more than one factor: <c>mfa</c> is present (a verified passkey), or both a password and a one-time code were used (password plus two-factor code).</summary>
    public static bool IsMultiFactor(IEnumerable<string> amr)
    {
        var have = amr.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return have.Contains(MultiFactor) || (have.Contains(Password) && have.Contains(OneTimeCode));
    }
}

/// <summary>Endpoint metadata: the request must carry a fresh code of the authenticator app (TOTP).</summary>
public interface IAuth2TotpMetadata
{
    /// <summary>
    /// False (<c>WithTotp()</c> / <c>[Auth2Totp]</c>): a code is <b>required</b>; a person without two-factor is refused with <c>totp_setup_required</c>.
    /// True (<c>WithOptionalTotp()</c> / <c>[Auth2OptionalTotp]</c>): only people who have two-factor on are asked; the others pass.
    /// </summary>
    bool Optional { get; }
}

/// <summary>
/// Where the code of <c>WithTotp()</c> / <c>[Auth2Totp]</c> is read from, and how a refused request tells the caller why.
/// <para>A refused request is <b>403</b>. The response header <see cref="ResponseHeader"/> holds the error code, and an API or AJAX request also gets the body <c>{ "code": "...", "message": "..." }</c>:</para>
/// <list type="bullet">
/// <item><c>totp_required</c>: the person has two-factor on and sent no code. Ask for one.</item>
/// <item><c>invalid_otp</c>: the code is wrong, expired or already used. Ask for the next one.</item>
/// <item><c>totp_setup_required</c>: the person has not turned on two-factor and the action needs it (not for an optional rule). Send them to turn it on.</item>
/// <item><c>locked_out</c>: too many wrong codes, the account is locked.</item>
/// <item><c>two_factor_not_allowed</c>: the project does not allow authenticator apps.</item>
/// <item><c>forbidden</c>: a machine token (a program has no authenticator app).</item>
/// <item><c>server_unavailable</c>: the Server could not be asked (the action is refused: fail closed).</item>
/// </list>
/// The messages of the Server are written in the language of the request (<c>Errors.resx</c>).
/// </summary>
public static class Auth2Totp
{
    /// <summary>The request header that carries the code (APIs and AJAX calls).</summary>
    public const string HeaderName = "X-Auth2-Totp";

    /// <summary>The form field that carries the code (HTML forms of controllers and Razor Pages).</summary>
    public const string FormField = "auth2_totp";

    /// <summary>The response header of a refused request: the error code (see the list above).</summary>
    public const string ResponseHeader = "X-Auth2-Totp-Status";

    internal const string ItemsKey = "Auth2.Totp.Outcome";

    /// <summary>The property of the sign-in request that asks the Server for a step-up (it becomes <c>acr_values</c>).</summary>
    internal const string StepUpItem = "auth2.step_up";

    /// <summary>
    /// A short-lived cookie that says "this browser was just sent to the Server for a code". If the request is refused again after coming back (the person went back without a code),
    /// the browser is not sent again: that would be an endless round trip.
    /// </summary>
    internal const string StepUpGuardCookie = ".SystemX.Auth2.StepUp";

    /// <summary>The code of a request that passed.</summary>
    internal const string OkCode = "ok";

    /// <summary>
    /// True when the session says that the person gave a code of the authenticator app (claim <c>step_up_at</c>, not in the future) and that it still counts (claim <c>step_up_until</c>,
    /// which the Server wrote from its setting <c>TwoFactor.StepUpMinutes</c>). The Client has no window of its own.
    /// </summary>
    internal static bool IsFresh(System.Security.Claims.ClaimsPrincipal user, DateTimeOffset now) =>
        long.TryParse(user.FindFirst(Auth2Claims.StepUpAt)?.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var at)
        && long.TryParse(user.FindFirst(Auth2Claims.StepUpUntil)?.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var until)
        && at <= now.ToUnixTimeSeconds() && now.ToUnixTimeSeconds() <= until;

    /// <summary>
    /// The page to come back to after the step-up: this page when the request was a GET, otherwise the page the person came from (a form cannot be sent again from a redirect),
    /// otherwise the home page. Always a path of this application.
    /// </summary>
    internal static string BackAfterStepUp(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method)) return Auth2Http.SafeReturnUrl(request.PathBase + request.Path + request.QueryString);
        return Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var from) && string.Equals(from.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
            ? Auth2Http.SafeReturnUrl(from.PathAndQuery)
            : "/";
    }
}

/// <summary>What the check of the code said, kept for the request: <see cref="Auth2Totp.OkCode"/> or an error code with the message of the Server.</summary>
internal sealed record Auth2TotpOutcome(string Code, string? Message);

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

/// <summary>
/// "One of these is enough" across kinds: the rule passes when the person has any of the roles, or any of the permissions, or is in any of the groups,
/// or the token has any of the scopes. Any list may be empty. Written by <c>[Auth2AnyOf]</c>, <c>[Auth2RoleOrPermission]</c> and the other attributes of this family,
/// and by <c>WithAnyOf</c> and its shortcuts. The scanners read it, so the roles, permissions, groups and scopes in it are synced like any other.
/// </summary>
public interface IAuth2AnyOfMetadata
{
    /// <summary>Roles. The rule passes when the user has any of them.</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>Permissions. The rule passes when the user has any of them.</summary>
    IReadOnlyList<string> Permissions { get; }

    /// <summary>Groups. The rule passes when the user is in any of them.</summary>
    IReadOnlyList<string> Groups { get; }

    /// <summary>Scopes. The rule passes when the token has any of them.</summary>
    IReadOnlyList<string> Scopes { get; }
}

internal static class PolicyNames
{
    public const string Prefix = "auth2:";
    public const char Separator = '|';

    public static string Make(string kind, string mode, IEnumerable<string> values) => $"{Prefix}{kind}:{mode}:{string.Join(Separator, values)}";

    /// <summary>
    /// <c>auth2:any:any:r=Admin|Editor;p=orders.write;g=finance;s=reports.read</c>. The kinds are separated by <c>;</c>, the values by <c>|</c>.
    /// None of these characters is allowed in a role name, permission key, group name or scope, so the name can be taken apart again.
    /// </summary>
    public static string MakeAnyOf(IEnumerable<string> roles, IEnumerable<string> permissions, IEnumerable<string> groups, IEnumerable<string> scopes)
    {
        var parts = new List<string>();
        void Add(string key, IEnumerable<string> values) { var list = values.ToList(); if (list.Count > 0) parts.Add(key + "=" + string.Join(Separator, list)); }
        Add("r", roles); Add("p", permissions); Add("g", groups); Add("s", scopes);
        return $"{Prefix}any:any:{string.Join(';', parts)}";
    }
}

/// <summary>The user must be signed in (a valid token). Use on controllers, Razor Page models and Blazor components.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2AuthorizeAttribute : AuthorizeAttribute;

/// <summary>Anyone may call this, even without a token.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2AnonymousAttribute : Attribute, IAllowAnonymous;

/// <summary>
/// On a class or method: the user needs a permission (one of the keys, or all of them with <see cref="RequireAll"/>).
/// On a <c>const string</c> field (with <c>AddFromClass</c>) or an enum member (with <c>AddFromEnum</c>): no keys, only the data of the permission
/// (<see cref="DisplayName"/>, <see cref="Description"/>); the value of the field is the key.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public sealed class Auth2PermissionAttribute : AuthorizeAttribute, IAuth2PermissionMetadata
{
    private readonly bool _all;

    /// <summary>Needs any of the permissions. With no key (on a field) it only carries the data of the permission.</summary>
    public Auth2PermissionAttribute(params string[] permissions) : this(false, permissions) { }

    /// <summary>Needs any (or all) of the permissions.</summary>
    public Auth2PermissionAttribute(bool requireAll, params string[] permissions)
    {
        Permissions = [.. permissions];
        _all = requireAll;
        if (permissions.Length > 0) Policy = PolicyNames.Make("perm", requireAll ? "all" : "any", permissions);
    }

    /// <summary>Display name of the permission (on a field or enum member).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Description of the permission (on a field or enum member).</summary>
    public string? Description { get; set; }

    /// <summary>Group label for screens. Without it the category (the class or enum name) is used.</summary>
    public string? Group { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<string> Permissions { get; }

    /// <inheritdoc />
    public bool RequireAll => _all;
}

/// <summary>
/// On a class or method: the user needs a role (one of the names, or all with <c>requireAll</c>).
/// On a <c>const string</c> field or an enum member: no names, only the data of the role (<see cref="DisplayName"/>, <see cref="Description"/>, <see cref="Permissions"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public sealed class Auth2RoleAttribute : AuthorizeAttribute, IAuth2RoleMetadata
{
    private readonly bool _all;

    /// <summary>Needs any of the roles. With no name (on a field) it only carries the data of the role.</summary>
    public Auth2RoleAttribute(params string[] roles) : this(false, roles) { }

    /// <summary>Needs any (or all) of the roles.</summary>
    public Auth2RoleAttribute(bool requireAll, params string[] roles)
    {
        RoleNames = [.. roles];
        _all = requireAll;
        if (roles.Length > 0) Policy = PolicyNames.Make("role", requireAll ? "all" : "any", roles);
    }

    /// <summary>Display name of the role (on a field or enum member).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Description of the role (on a field or enum member).</summary>
    public string? Description { get; set; }

    /// <summary>Permission keys the role carries (on a field). Not possible with string or enum sources.</summary>
    public string[]? Permissions { get; set; }

    /// <summary>The role names.</summary>
    public IReadOnlyList<string> RoleNames { get; }

    IReadOnlyList<string> IAuth2RoleMetadata.Roles => RoleNames;

    /// <inheritdoc />
    public bool RequireAll => _all;
}

/// <summary>
/// On a class or method: the token needs one of these scopes.
/// On a <c>const string</c> field or an enum member: no names, only the data of the scope (<see cref="DisplayName"/>, <see cref="Description"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public sealed class Auth2ScopeAttribute : AuthorizeAttribute, IAuth2ScopeMetadata
{
    /// <summary>Needs any of the scopes. With no name (on a field) it only carries the data of the scope.</summary>
    public Auth2ScopeAttribute(params string[] scopes)
    {
        Scopes = [.. scopes];
        if (scopes.Length > 0) Policy = PolicyNames.Make("scope", "any", scopes);
    }

    /// <summary>Display name of the scope (on a field or enum member).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Description of the scope (on a field or enum member).</summary>
    public string? Description { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<string> Scopes { get; }
}

/// <summary>
/// On a class or method: the user must be in one of these groups (or in all of them, with <c>requireAll: true</c>).
/// On a <c>const string</c> field or an enum member: no names, only the data of the group (<see cref="DisplayName"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public sealed class Auth2GroupAttribute : AuthorizeAttribute, IAuth2GroupMetadata
{
    /// <summary>Needs any of the groups. With no name (on a field) it only carries the data of the group.</summary>
    public Auth2GroupAttribute(params string[] groups) : this(false, groups) { }

    /// <summary>Needs all of the groups (<paramref name="requireAll"/> true) or any of them (false).</summary>
    public Auth2GroupAttribute(bool requireAll, params string[] groups)
    {
        Groups = [.. groups];
        RequireAll = requireAll;
        if (groups.Length > 0) Policy = PolicyNames.Make("group", requireAll ? "all" : "any", groups);
    }

    /// <summary>Display name of the group (on a field or enum member).</summary>
    public string? DisplayName { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<string> Groups { get; }

    /// <inheritdoc />
    public bool RequireAll { get; }
}

/// <summary>
/// The person must have signed in with one of these methods (the <c>amr</c> claim): <c>pwd</c> (password), <c>otp</c> (one-time code), <c>swk</c> / <c>hwk</c> (a passkey), <c>mfa</c>.
/// Use it for a sensitive endpoint that needs a stronger sign-in than a password. A machine token has no sign-in method, so it is refused. See <see cref="Auth2SignInMethods"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2SignInMethodAttribute : AuthorizeAttribute, IAuth2SignInMethodMetadata
{
    /// <summary>Needs any of the methods.</summary>
    public Auth2SignInMethodAttribute(params string[] methods)
    {
        if (methods.Length == 0) throw new ArgumentException("At least one sign-in method is needed.", nameof(methods));
        Methods = [.. methods];
        Policy = PolicyNames.Make("amr", "any", methods);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Methods { get; }

    /// <inheritdoc />
    public bool MultiFactor => false;
}

/// <summary>
/// The request must carry a <b>fresh code of the authenticator app</b> (TOTP), checked on the Server every time. For actions that need the person to be there right now: a transfer,
/// deleting data, changing security settings. The code is read from the header <c>X-Auth2-Totp</c> or the form field <c>auth2_totp</c> (<see cref="Auth2Totp"/>).
/// A code works <b>once</b>. The code is <b>required</b>: a person who has not turned on two-factor is refused with the error <c>totp_setup_required</c>
/// (use <see cref="Auth2OptionalTotpAttribute"/> to ask only the people who have it). Without a code the error is <c>totp_required</c>, with a wrong one <c>invalid_otp</c>.
/// 403 with the header <c>X-Auth2-Totp-Status</c> (and a JSON body for API requests). A machine token is refused.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2TotpAttribute : AuthorizeAttribute, IAuth2TotpMetadata
{
    /// <summary>Creates the attribute.</summary>
    public Auth2TotpAttribute() => Policy = PolicyNames.Make("totp", "required", []);

    /// <inheritdoc />
    public bool Optional => false;
}

/// <summary>
/// Like <see cref="Auth2TotpAttribute"/>, but the code is <b>optional</b> for people who have not turned on two-factor: they pass without a code (there is nothing to check), and a person
/// who <b>has</b> two-factor on must give a code. Use it where two-factor is a choice, not a rule.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2OptionalTotpAttribute : AuthorizeAttribute, IAuth2TotpMetadata
{
    /// <summary>Creates the attribute.</summary>
    public Auth2OptionalTotpAttribute() => Policy = PolicyNames.Make("totp", "optional", []);

    /// <inheritdoc />
    public bool Optional => true;
}

/// <summary>The person must have used more than one factor to sign in: a passkey unlocked with a fingerprint, face or PIN, or a password plus a one-time code. See <see cref="Auth2SignInMethods.IsMultiFactor"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class Auth2MultiFactorAttribute : AuthorizeAttribute, IAuth2SignInMethodMetadata
{
    /// <summary>Creates the attribute.</summary>
    public Auth2MultiFactorAttribute() => Policy = PolicyNames.Make("amr", "mfa", []);

    /// <inheritdoc />
    public IReadOnlyList<string> Methods => [];

    /// <inheritdoc />
    public bool MultiFactor => true;
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

/// <summary>
/// Base of the "any of" attributes. It is <see cref="IAuthorizeData"/> itself (not <c>AuthorizeAttribute</c>) because the policy name is made from the named
/// properties (<c>Roles = ...</c>), which are set after the constructor ran: the name is built when ASP.NET reads <see cref="Policy"/>.
/// </summary>
public abstract class Auth2AnyOfAttributeBase : Attribute, IAuthorizeData, IAuth2AnyOfMetadata
{
    private string[] _roles = [], _permissions = [], _groups = [], _scopes = [];

    /// <summary>Roles of the rule, or empty. Used by the attributes that have roles.</summary>
    protected string[] RoleValues { get => _roles; set => _roles = value ?? []; }

    /// <summary>Permissions of the rule, or empty.</summary>
    protected string[] PermissionValues { get => _permissions; set => _permissions = value ?? []; }

    /// <summary>Groups of the rule, or empty.</summary>
    protected string[] GroupValues { get => _groups; set => _groups = value ?? []; }

    /// <summary>Scopes of the rule, or empty.</summary>
    protected string[] ScopeValues { get => _scopes; set => _scopes = value ?? []; }

    IReadOnlyList<string> IAuth2AnyOfMetadata.Roles => _roles;
    IReadOnlyList<string> IAuth2AnyOfMetadata.Permissions => _permissions;
    IReadOnlyList<string> IAuth2AnyOfMetadata.Groups => _groups;
    IReadOnlyList<string> IAuth2AnyOfMetadata.Scopes => _scopes;

    /// <summary>The policy of ASP.NET, made from the values. <c>null</c> when there is no value at all (then the rule means "a signed-in user").</summary>
    public string? Policy
    {
        get => _roles.Length + _permissions.Length + _groups.Length + _scopes.Length == 0 ? null : PolicyNames.MakeAnyOf(_roles, _permissions, _groups, _scopes);
        set { /* the name is always made from the values */ }
    }

    string? IAuthorizeData.Roles { get => null; set { } }

    /// <summary>Not used.</summary>
    public string? AuthenticationSchemes { get; set; }
}

/// <summary>
/// The rule passes when the user has <b>any</b> of the roles <b>or any</b> of the permissions <b>or</b> is in <b>any</b> of the groups <b>or</b> the token has <b>any</b> of the scopes.
/// Set the lists you need: <c>[Auth2AnyOf(Roles = new[] { "Admin" }, Permissions = new[] { "orders.write" })]</c>. Two attributes are two rules (AND).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2AnyOfAttribute : Auth2AnyOfAttributeBase
{
    /// <summary>Roles (any).</summary>
    public string[] Roles { get => RoleValues; set => RoleValues = value; }

    /// <summary>Permissions (any).</summary>
    public string[] Permissions { get => PermissionValues; set => PermissionValues = value; }

    /// <summary>Groups (any).</summary>
    public string[] Groups { get => GroupValues; set => GroupValues = value; }

    /// <summary>Scopes of the token (any).</summary>
    public string[] Scopes { get => ScopeValues; set => ScopeValues = value; }
}

/// <summary>The user has any of the <see cref="Roles"/> <b>or</b> any of the <see cref="Permissions"/>. Example: an admin, or whoever may write orders.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2RoleOrPermissionAttribute : Auth2AnyOfAttributeBase
{
    /// <summary>Roles (any).</summary>
    public string[] Roles { get => RoleValues; set => RoleValues = value; }

    /// <summary>Permissions (any).</summary>
    public string[] Permissions { get => PermissionValues; set => PermissionValues = value; }
}

/// <summary>The user has any of the <see cref="Roles"/> <b>or</b> is in any of the <see cref="Groups"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2RoleOrGroupAttribute : Auth2AnyOfAttributeBase
{
    /// <summary>Roles (any).</summary>
    public string[] Roles { get => RoleValues; set => RoleValues = value; }

    /// <summary>Groups (any).</summary>
    public string[] Groups { get => GroupValues; set => GroupValues = value; }
}

/// <summary>The user has any of the <see cref="Permissions"/> <b>or</b> is in any of the <see cref="Groups"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2PermissionOrGroupAttribute : Auth2AnyOfAttributeBase
{
    /// <summary>Permissions (any).</summary>
    public string[] Permissions { get => PermissionValues; set => PermissionValues = value; }

    /// <summary>Groups (any).</summary>
    public string[] Groups { get => GroupValues; set => GroupValues = value; }
}

/// <summary>The token has any of the <see cref="Scopes"/> <b>or</b> the user has any of the <see cref="Roles"/>. Example: a program with the scope, or an admin.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class Auth2ScopeOrRoleAttribute : Auth2AnyOfAttributeBase
{
    /// <summary>Scopes of the token (any).</summary>
    public string[] Scopes { get => ScopeValues; set => ScopeValues = value; }

    /// <summary>Roles (any).</summary>
    public string[] Roles { get => RoleValues; set => RoleValues = value; }
}
