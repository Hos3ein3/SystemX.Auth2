using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Client.Authorization;

internal enum Auth2RequirementKind { Permission, Role, Scope, Group, MachineOnly, UserOnly }

/// <summary>One rule of an Auth2 policy. Several rules on one endpoint must all pass.</summary>
internal sealed record Auth2Requirement(Auth2RequirementKind Kind, bool RequireAll, IReadOnlyList<string> Values) : IAuthorizationRequirement;

/// <summary>
/// Checks the Auth2 requirements. Roles, scopes and the token kind come from the claims of the token. Permissions and groups come from the
/// access data (cached, from the server). When the server cannot be asked and nothing is cached, the answer is "no" (fail closed).
/// </summary>
internal sealed class Auth2AuthorizationHandler(IAuth2AccessProvider access, ILogger<Auth2AuthorizationHandler> log) : AuthorizationHandler<Auth2Requirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, Auth2Requirement requirement)
    {
        var user = context.User;
        var ok = requirement.Kind switch
        {
            Auth2RequirementKind.MachineOnly => user.FindFirst(Auth2Claims.Machine)?.Value == "true",
            Auth2RequirementKind.UserOnly => user.FindFirst(Auth2Claims.Machine)?.Value != "true",
            Auth2RequirementKind.Scope => Any(ScopeClaims.Read(user), requirement.Values, StringComparer.Ordinal),
            Auth2RequirementKind.Role => Check(await RolesAsync(user), requirement, StringComparer.OrdinalIgnoreCase),
            _ => await CheckAccessAsync(context, requirement),
        };
        if (ok) context.Succeed(requirement);
        else context.Fail(new AuthorizationFailureReason(this, $"Auth2 requirement {requirement.Kind} not met."));
    }

    /// <summary>Roles from the claims. A principal with no role claim (a cookie session that loads access lazily) gets its roles from the access data.</summary>
    private async Task<IReadOnlyList<string>> RolesAsync(System.Security.Claims.ClaimsPrincipal user)
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

    private async Task<bool> CheckAccessAsync(AuthorizationHandlerContext context, Auth2Requirement requirement)
    {
        Auth2Access data;
        try { data = await access.GetAccessAsync(context.User); }
        catch (Auth2Exception ex)
        {
            log.LogWarning("Access was refused because the Auth2 server could not be asked ({Code}).", ex.ErrorCode);
            return false;
        }

        if (requirement.Kind == Auth2RequirementKind.Group) return Any(data.Groups, requirement.Values, StringComparer.OrdinalIgnoreCase);

        // Permission keys are lowercase on the server.
        var source = (context.Resource as HttpContext)?.GetEndpoint()?.Metadata.GetMetadata<IAuth2PermissionSourceMetadata>()?.Source ?? Auth2PermissionSource.Any;
        var have = source switch
        {
            Auth2PermissionSource.RoleOnly => data.RolePermissions,
            Auth2PermissionSource.UserOnly => data.DirectPermissions,
            _ => data.Permissions,
        };
        return Check(have, new Auth2Requirement(requirement.Kind, requirement.RequireAll, [.. requirement.Values.Select(v => v.Trim().ToLowerInvariant())]), StringComparer.Ordinal);
    }

    private static bool Any(IEnumerable<string> have, IEnumerable<string> wanted, StringComparer comparer) => wanted.Any(w => have.Contains(w, comparer));

    private static bool Check(IReadOnlyList<string> have, Auth2Requirement r, StringComparer comparer) =>
        r.RequireAll ? r.Values.All(v => have.Contains(v, comparer)) : r.Values.Any(v => have.Contains(v, comparer));
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
        var values = parts.Length > 2 && parts[2].Length > 0 ? parts[2].Split(PolicyNames.Separator) : [];
        var kind = parts[0] switch
        {
            "perm" => Auth2RequirementKind.Permission,
            "role" => Auth2RequirementKind.Role,
            "scope" => Auth2RequirementKind.Scope,
            "group" => Auth2RequirementKind.Group,
            "kind" => parts[1] == "machine" ? Auth2RequirementKind.MachineOnly : Auth2RequirementKind.UserOnly,
            _ => throw new InvalidOperationException($"Unknown Auth2 policy '{name}'."),
        };
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new Auth2Requirement(kind, parts[1] == "all", values)).Build();
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
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
