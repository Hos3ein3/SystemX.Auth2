using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Client.Authorization;

internal enum Auth2RequirementKind { Permission, Role, Scope, Group, MachineOnly, UserOnly, SignInMethod, MultiFactor, Totp, OptionalTotp }

/// <summary>One rule of an Auth2 policy. Several rules on one endpoint must all pass.</summary>
internal sealed record Auth2Requirement(Auth2RequirementKind Kind, bool RequireAll, IReadOnlyList<string> Values) : IAuthorizationRequirement;

/// <summary>"Any of these": roles, permissions, groups and scopes together. One match of any kind is enough.</summary>
internal sealed record Auth2AnyOfRequirement(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Groups, IReadOnlyList<string> Scopes) : IAuthorizationRequirement;

/// <summary>
/// Checks the Auth2 requirements. Roles, scopes and the token kind come from the claims of the token. Permissions and groups come from the
/// access data (cached, from the server). When the server cannot be asked and nothing is cached, the answer is "no" (fail closed).
/// </summary>
internal sealed class Auth2AuthorizationHandler(IAuth2AccessProvider access, ILogger<Auth2AuthorizationHandler> log) : AuthorizationHandler<Auth2Requirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, Auth2Requirement requirement)
    {
        if (requirement.Kind is Auth2RequirementKind.Totp or Auth2RequirementKind.OptionalTotp) return;   // the code is checked by Auth2TotpAuthorizationHandler (it needs the request)
        var user = context.User;
        var ok = requirement.Kind switch
        {
            Auth2RequirementKind.MachineOnly => user.FindFirst(Auth2Claims.Machine)?.Value == "true",
            Auth2RequirementKind.UserOnly => user.FindFirst(Auth2Claims.Machine)?.Value != "true",
            Auth2RequirementKind.Scope => Any(ScopeClaims.Read(user), requirement.Values, StringComparer.Ordinal),
            // how the person signed in (the amr claim); a machine token has none, so it never passes
            Auth2RequirementKind.SignInMethod => Any(user.FindAll(Auth2Claims.Amr).Select(c => c.Value).ToList(), requirement.Values, StringComparer.OrdinalIgnoreCase),
            Auth2RequirementKind.MultiFactor => Auth2SignInMethods.IsMultiFactor(user.FindAll(Auth2Claims.Amr).Select(c => c.Value)),
            Auth2RequirementKind.Role => Check(await RolesAsync(user), requirement, StringComparer.OrdinalIgnoreCase),
            _ => await CheckAccessAsync(context, requirement),
        };
        if (ok) context.Succeed(requirement);
        else context.Fail(new AuthorizationFailureReason(this, $"Auth2 requirement {requirement.Kind} not met."));
    }

    private Task<IReadOnlyList<string>> RolesAsync(System.Security.Claims.ClaimsPrincipal user) => Auth2Checks.RolesAsync(user, access, log);

    private async Task<bool> CheckAccessAsync(AuthorizationHandlerContext context, Auth2Requirement requirement)
    {
        Auth2Access data;
        try { data = await access.GetAccessAsync(context.User); }
        catch (Auth2Exception ex)
        {
            log.LogWarning("Access was refused because the Auth2 server could not be asked ({Code}).", ex.ErrorCode);
            return false;
        }

        if (requirement.Kind == Auth2RequirementKind.Group) return Check(data.Groups, requirement, StringComparer.OrdinalIgnoreCase);

        // Permission keys are lowercase on the server.
        var have = Auth2Checks.PermissionsOf(data, context.Resource);
        return Check(have, new Auth2Requirement(requirement.Kind, requirement.RequireAll, [.. requirement.Values.Select(v => v.Trim().ToLowerInvariant())]), StringComparer.Ordinal);
    }

    private static bool Any(IEnumerable<string> have, IEnumerable<string> wanted, StringComparer comparer) => wanted.Any(w => have.Contains(w, comparer));

    private static bool Check(IReadOnlyList<string> have, Auth2Requirement r, StringComparer comparer) =>
        r.RequireAll ? r.Values.All(v => have.Contains(v, comparer)) : r.Values.Any(v => have.Contains(v, comparer));
}

/// <summary>
/// Checks the code of <c>WithTotp()</c> and <c>WithOptionalTotp()</c>. The code comes from the request (header or form field, it may be missing); the Server decides
/// (<see cref="IAuth2ApiClientService.VerifyTotpAsync"/>), once per request: it knows whether the person has two-factor on, so it can tell "no code" from "no authenticator app",
/// and it answers with the error code and the message in the language of the request. Everything that goes wrong is a refusal (fail closed).
/// </summary>
internal sealed class Auth2TotpAuthorizationHandler(IHttpContextAccessor http, IAuth2AccessTokenAccessor tokens, IAuth2ApiClientService api, ILogger<Auth2TotpAuthorizationHandler> log)
    : AuthorizationHandler<Auth2Requirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, Auth2Requirement requirement)
    {
        if (requirement.Kind is not (Auth2RequirementKind.Totp or Auth2RequirementKind.OptionalTotp)) return;
        var request = context.Resource as HttpContext ?? http.HttpContext;
        var outcome = await OutcomeAsync(context, request, optional: requirement.Kind == Auth2RequirementKind.OptionalTotp);
        if (request is not null) request.Items[Auth2Totp.ItemsKey] = outcome;
        if (outcome.Code == Auth2Totp.OkCode) context.Succeed(requirement);
        else context.Fail(new AuthorizationFailureReason(this, $"Auth2 TOTP not accepted ({outcome.Code})."));
    }

    private async Task<Auth2TotpOutcome> OutcomeAsync(AuthorizationHandlerContext context, HttpContext? request, bool optional)
    {
        if (context.User.FindFirst(Auth2Claims.Machine)?.Value == "true" || request is null)
            return new(Auth2ErrorCodes.Forbidden, "A program has no authenticator app. This action is for people.");

        // The same request is asked once: a code works once, so a second evaluation (another policy check) must not ask the Server again.
        if (request.Items[Auth2Totp.ItemsKey] is Auth2TotpOutcome known) return known;

        var code = request.Request.Headers[Auth2Totp.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(code) && request.Request.HasFormContentType)
            code = (await request.Request.ReadFormAsync(request.RequestAborted))[Auth2Totp.FormField].ToString();

        var token = await tokens.GetAccessTokenAsync(request.RequestAborted);
        if (token is null) return new(Auth2ErrorCodes.InvalidToken, "There is no access token in this request.");
        try
        {
            var result = await api.VerifyTotpAsync(new TwoFactorVerifyRequest((code ?? "").Trim(), optional), request.RequestAborted);
            return result.IsSuccess ? new(Auth2Totp.OkCode, null) : new(result.Error?.Code ?? Auth2ErrorCodes.InvalidOtp, result.Error?.Message);
        }
        catch (Auth2Exception ex)
        {
            log.LogWarning("The TOTP code could not be checked because the Auth2 server could not be asked ({Code}).", ex.ErrorCode);
            return new(Auth2ClientErrorCodes.ServerUnavailable, "The Auth2 server could not be asked, so the action was refused.");
        }
    }
}

/// <summary>What the handlers share: where roles come from, and which permission list counts on this endpoint.</summary>
internal static class Auth2Checks
{
    /// <summary>Roles from the claims. A principal with no role claim (a cookie session that loads access lazily, or a token without the <c>roles</c> scope) gets its roles from the access data.</summary>
    public static async Task<IReadOnlyList<string>> RolesAsync(System.Security.Claims.ClaimsPrincipal user, IAuth2AccessProvider access, ILogger log)
    {
        var claims = user.FindAll("role").Concat(user.FindAll(System.Security.Claims.ClaimTypes.Role)).Select(c => c.Value).Distinct().ToList();
        if (claims.Count > 0 || user.Identity?.IsAuthenticated != true) return claims;
        try { return (await access.GetAccessAsync(user)).Roles; }
        catch (Auth2Exception ex)
        {
            log.LogWarning("The roles of the user could not be read from the Auth2 server ({Code}).", ex.ErrorCode);
            return claims;
        }
    }

    /// <summary>The permission list that counts: all, only those from roles, or only direct grants, by the metadata of the endpoint.</summary>
    public static IReadOnlyList<string> PermissionsOf(Auth2Access data, object? resource)
    {
        var source = (resource as HttpContext)?.GetEndpoint()?.Metadata.GetMetadata<IAuth2PermissionSourceMetadata>()?.Source ?? Auth2PermissionSource.Any;
        return source switch
        {
            Auth2PermissionSource.RoleOnly => data.RolePermissions,
            Auth2PermissionSource.UserOnly => data.DirectPermissions,
            _ => data.Permissions,
        };
    }
}

/// <summary>
/// Checks "any of" rules. The cheap checks come first (scopes and token roles need no call), so the Server is asked for permissions and groups
/// only when nothing matched yet. When it cannot be asked and nothing is cached, those two parts count as "no" (fail closed); a scope or role that matched still passes.
/// </summary>
internal sealed class Auth2AnyOfHandler(IAuth2AccessProvider access, ILogger<Auth2AnyOfHandler> log) : AuthorizationHandler<Auth2AnyOfRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, Auth2AnyOfRequirement r)
    {
        if (await PassesAsync(context, r)) context.Succeed(r);
        else context.Fail(new AuthorizationFailureReason(this, "None of the Auth2 'any of' values matched."));
    }

    private async Task<bool> PassesAsync(AuthorizationHandlerContext context, Auth2AnyOfRequirement r)
    {
        var user = context.User;
        if (r.Scopes.Count > 0 && r.Scopes.Any(s => ScopeClaims.Read(user).Contains(s, StringComparer.Ordinal))) return true;
        if (r.Roles.Count > 0)
        {
            var roles = await Auth2Checks.RolesAsync(user, access, log);
            if (r.Roles.Any(x => roles.Contains(x, StringComparer.OrdinalIgnoreCase))) return true;
        }
        if (r.Permissions.Count == 0 && r.Groups.Count == 0) return false;

        Auth2Access data;
        try { data = await access.GetAccessAsync(user); }
        catch (Auth2Exception ex)
        {
            log.LogWarning("Access was refused because the Auth2 server could not be asked ({Code}).", ex.ErrorCode);
            return false;
        }
        if (r.Groups.Any(g => data.Groups.Contains(g, StringComparer.OrdinalIgnoreCase))) return true;
        var have = Auth2Checks.PermissionsOf(data, context.Resource);
        return r.Permissions.Any(p => have.Contains(p.Trim().ToLowerInvariant(), StringComparer.Ordinal));
    }
}

/// <summary>Builds policies from the names that the attributes and extensions make (<c>auth2:perm:any:orders.read|orders.write</c>). Other names go to the normal provider, so your own policies still work.</summary>
internal sealed class Auth2PolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _built = new();

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyNames.Prefix, StringComparison.Ordinal)) return base.GetPolicyAsync(policyName);
        return Task.FromResult<AuthorizationPolicy?>(_built.GetOrAdd(policyName, Build));
    }

    private static AuthorizationPolicy Build(string name)
    {
        var parts = name[PolicyNames.Prefix.Length..].Split(':', 3);
        if (parts[0] == "any") return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(AnyOf(parts.Length > 2 ? parts[2] : "")).Build();
        var values = parts.Length > 2 && parts[2].Length > 0 ? parts[2].Split(PolicyNames.Separator) : [];
        var kind = parts[0] switch
        {
            "perm" => Auth2RequirementKind.Permission,
            "role" => Auth2RequirementKind.Role,
            "scope" => Auth2RequirementKind.Scope,
            "group" => Auth2RequirementKind.Group,
            "kind" => parts[1] == "machine" ? Auth2RequirementKind.MachineOnly : Auth2RequirementKind.UserOnly,
            "amr" => parts[1] == "mfa" ? Auth2RequirementKind.MultiFactor : Auth2RequirementKind.SignInMethod,
            "totp" => parts[1] == "optional" ? Auth2RequirementKind.OptionalTotp : Auth2RequirementKind.Totp,
            _ => throw new InvalidOperationException($"Unknown Auth2 policy '{name}'."),
        };
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new Auth2Requirement(kind, parts[1] == "all", values)).Build();
    }

    /// <summary>Takes <c>r=Admin|Editor;p=orders.write;g=finance;s=reports.read</c> apart again.</summary>
    private static Auth2AnyOfRequirement AnyOf(string text)
    {
        var lists = new Dictionary<string, string[]> { ["r"] = [], ["p"] = [], ["g"] = [], ["s"] = [] };
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.IndexOf('=');
            if (at > 0 && lists.ContainsKey(part[..at])) lists[part[..at]] = part[(at + 1)..].Split(PolicyNames.Separator, StringSplitOptions.RemoveEmptyEntries);
        }
        return new Auth2AnyOfRequirement(lists["r"], lists["p"], lists["g"], lists["s"]);
    }
}

/// <summary>What happened to a request that reached an endpoint marked with <c>WithAuth2Audit</c> / <c>[Auth2Audit]</c>.</summary>
/// <param name="Action">The event name.</param>
/// <param name="Succeeded">True when access was granted.</param>
/// <param name="UserId">The user, or null (machine token, nobody).</param>
/// <param name="IsMachine">True for a machine token.</param>
/// <param name="Path">The request path (no query string).</param>
/// <param name="AtUtc">When.</param>
public sealed record Auth2AuthorizationEvent(string Action, bool Succeeded, Guid? UserId, bool IsMachine, string Path, DateTimeOffset AtUtc);

/// <summary>Receives audit events of authorization. Register your own to write them to your audit log. The default writes a log line.</summary>
public interface IAuth2AuditSink
{
    /// <summary>Called for every authorization decision of an endpoint that has an audit action.</summary>
    Task OnAuthorizationAsync(Auth2AuthorizationEvent evt, CancellationToken ct = default);
}

internal sealed class LoggingAuditSink(ILogger<LoggingAuditSink> log) : IAuth2AuditSink
{
    public Task OnAuthorizationAsync(Auth2AuthorizationEvent evt, CancellationToken ct = default)
    {
        log.LogInformation("Auth2 audit {Action}: {Result} (user {UserId}, machine {IsMachine}) {Path}", evt.Action, evt.Succeeded ? "allowed" : "refused", evt.UserId, evt.IsMachine, evt.Path);
        return Task.CompletedTask;
    }
}

/// <summary>Raises the audit event, then does what ASP.NET does: continue, 401 or 403.</summary>
internal sealed class Auth2AuthorizationResultHandler(IAuth2AuditSink sink) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAuth2AuditMetadata>() is { } audit)
        {
            var user = context.User;
            var machine = user.FindFirst(Auth2Claims.Machine)?.Value == "true";
            Guid? id = !machine && Guid.TryParse(user.FindFirst("sub")?.Value, out var g) ? g : null;
            await sink.OnAuthorizationAsync(new Auth2AuthorizationEvent(audit.Action, authorizeResult.Succeeded, id, machine, context.Request.Path.Value ?? "", DateTimeOffset.UtcNow), context.RequestAborted);
        }
        // A refused TOTP rule tells the caller why (no code, a wrong code, two-factor not turned on...): the error code in a header, and for an API or AJAX call also the body.
        if (!authorizeResult.Succeeded && context.Items[Auth2Totp.ItemsKey] is Auth2TotpOutcome totp && totp.Code != Auth2Totp.OkCode && !context.Response.HasStarted)
        {
            context.Response.Headers[Auth2Totp.ResponseHeader] = totp.Code;
            if (Auth2Http.IsApiRequest(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { code = totp.Code, message = totp.Message });
                return;
            }
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
