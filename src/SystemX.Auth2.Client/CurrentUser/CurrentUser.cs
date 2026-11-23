using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Contracts.Users;

namespace SystemX.Auth2.Client.CurrentUser;

/// <summary>
/// The signed-in user. Name, email, scopes and "is it a machine?" come from the token (no call). Roles, permissions and groups are
/// <b>async</b> because they may need a call to the server (<c>/api/auth2/me</c>); the answer is cached.
/// A machine token (client credentials) has no user: <see cref="IsMachine"/> is true and the access lists are empty.
/// </summary>
public interface ICurrentUser
{
    /// <summary>True when the request has a valid token.</summary>
    bool IsAuthenticated { get; }
    /// <summary>User id (<c>sub</c>), or null for a machine token or no user.</summary>
    Guid? UserId { get; }
    /// <summary>User name.</summary>
    string? UserName { get; }
    /// <summary>Email.</summary>
    string? Email { get; }
    /// <summary>Phone number.</summary>
    string? PhoneNumber { get; }
    /// <summary>First name.</summary>
    string? FirstName { get; }
    /// <summary>Last name.</summary>
    string? LastName { get; }
    /// <summary>Full name.</summary>
    string? FullName { get; }
    /// <summary>Scopes of the token.</summary>
    IReadOnlyList<string> Scopes { get; }
    /// <summary>True when the token belongs to a program (client credentials), not a person.</summary>
    bool IsMachine { get; }

    /// <summary>Roles. Empty for a machine token.</summary>
    Task<IReadOnlyList<string>> GetRolesAsync(CancellationToken ct = default);
    /// <summary>Permissions (effective). Empty for a machine token.</summary>
    Task<IReadOnlyList<string>> GetPermissionsAsync(CancellationToken ct = default);
    /// <summary>Groups. Empty for a machine token.</summary>
    Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken ct = default);
    /// <summary>True when the user has the permission.</summary>
    Task<bool> HasPermissionAsync(string permission, CancellationToken ct = default);
    /// <summary>True when the user has the role.</summary>
    Task<bool> HasRoleAsync(string role, CancellationToken ct = default);
}

/// <summary>The access data of one user that the client keeps. The two source lists can be empty with an older server.</summary>
public sealed record Auth2Access(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Groups,
    int AccessVersion, IReadOnlyList<string> DirectPermissions, IReadOnlyList<string> RolePermissions)
{
    /// <summary>No access.</summary>
    public static Auth2Access Empty { get; } = new([], [], [], 0, [], []);

    /// <summary>Makes it from the answer of <c>/me</c>.</summary>
    public static Auth2Access From(CurrentUserInfo me) =>
        new(me.Roles, me.Permissions, me.Groups, me.AccessVersion, me.DirectPermissions ?? [], me.RolePermissions ?? []);
}

/// <summary>Fetches the signed-in user from <c>/api/auth2/me</c>. It does <b>not</b> cache: call it after login and keep the result wherever you like.</summary>
public interface IAuth2CurrentUserService
{
    /// <summary>
    /// Asks the server for the user of the current request's access token (the <c>Authorization</c> header, or the token saved by the cookie login).
    /// A machine token gives a <c>forbidden</c> failure: there is no user.
    /// </summary>
    Task<Result<CurrentUserInfo>> FetchAsync(ClaimsPrincipal? principal = null, CancellationToken ct = default);

    /// <summary>Asks the server for the user of this access token.</summary>
    Task<Result<CurrentUserInfo>> FetchAsync(string accessToken, CancellationToken ct = default);
}

/// <summary>Keeps access data between requests. <see cref="MemoryCurrentUserStore"/> is the default; <see cref="DistributedCurrentUserStore"/> uses <c>IDistributedCache</c> (Redis).</summary>
public interface ICurrentUserStore
{
    /// <summary>Reads an entry, or null.</summary>
    Task<Auth2Access?> GetAsync(string key, CancellationToken ct = default);
    /// <summary>Saves an entry for <paramref name="ttl"/>.</summary>
    Task SetAsync(string key, Auth2Access access, TimeSpan ttl, CancellationToken ct = default);
    /// <summary>Removes an entry.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>Access data in the memory of this process.</summary>
public sealed class MemoryCurrentUserStore(IMemoryCache cache) : ICurrentUserStore
{
    /// <inheritdoc />
    public Task<Auth2Access?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(cache.TryGetValue(key, out Auth2Access? a) ? a : null);

    /// <inheritdoc />
    public Task SetAsync(string key, Auth2Access access, TimeSpan ttl, CancellationToken ct = default)
    {
        cache.Set(key, access, ttl);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>Access data in <c>IDistributedCache</c> (Redis, SQL Server, ...), so every instance of your application shares it.</summary>
public sealed class DistributedCurrentUserStore(IDistributedCache cache) : ICurrentUserStore
{
    /// <inheritdoc />
    public async Task<Auth2Access?> GetAsync(string key, CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is null) return null;
        try { return JsonSerializer.Deserialize<Auth2Access>(bytes); }
        catch (JsonException) { return null; }   // a broken entry is the same as no entry
    }

    /// <inheritdoc />
    public Task SetAsync(string key, Auth2Access access, TimeSpan ttl, CancellationToken ct = default) =>
        cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(access), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken ct = default) => cache.RemoveAsync(key, ct);
}

/// <summary>Finds the access token of the current request.</summary>
public interface IAuth2AccessTokenAccessor
{
    /// <summary>The bearer token of the request, or the token saved by the cookie sign-in, or null.</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
}

internal sealed class AccessTokenAccessor(IHttpContextAccessor http) : IAuth2AccessTokenAccessor
{
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var context = http.HttpContext;
        if (context is null) return null;
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return header["Bearer ".Length..].Trim();
        return await context.GetTokenAsync("access_token");   // saved by the cookie or OIDC sign-in (SaveTokens)
    }
}

internal sealed class CurrentUserService(IAuth2ApiClientService api, IAuth2AccessTokenAccessor tokens, IHttpContextAccessor http) : IAuth2CurrentUserService
{
    public async Task<Result<CurrentUserInfo>> FetchAsync(ClaimsPrincipal? principal = null, CancellationToken ct = default)
    {
        principal ??= http.HttpContext?.User;
        if (principal?.FindFirst(Auth2Claims.Machine)?.Value == "true") return Result<CurrentUserInfo>.Failure(Auth2ErrorCodes.Forbidden, "A machine token has no user.");
        var token = await tokens.GetAccessTokenAsync(ct);
        return token is null
            ? Result<CurrentUserInfo>.Failure(Auth2ErrorCodes.InvalidToken, "There is no access token in this request.")
            : await api.GetMeAsync(token, ct);
    }

    public Task<Result<CurrentUserInfo>> FetchAsync(string accessToken, CancellationToken ct = default) => api.GetMeAsync(accessToken, ct);
}

/// <summary>
/// Gets the access data of a principal: from the cache, else from the server. A cached entry is thrown away when its version is older than the
/// <c>pv</c> claim of the token, or when its time is over. Within one request the answer is kept (one call at most).
/// </summary>
public interface IAuth2AccessProvider
{
    /// <summary>
    /// The access data for the principal. Empty for a machine token or no user.
    /// </summary>
    /// <exception cref="Auth2Exception">The server could not be asked and nothing is cached.</exception>
    Task<Auth2Access> GetAccessAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

internal sealed class AccessProvider(IAuth2CurrentUserService users, ICurrentUserStore store, IOptions<Auth2CurrentUserOptions> options, IOptions<Auth2ClientOptions> client,
    ILogger<AccessProvider> log, TimeProvider clock) : IAuth2AccessProvider
{
    // One instance per request (scoped): a request asks the server at most once per user.
    private readonly Dictionary<string, Task<Auth2Access>> _perRequest = [];

    public Task<Auth2Access> GetAccessAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.FindFirst(Auth2Claims.Machine)?.Value == "true") return Task.FromResult(Auth2Access.Empty);
        var sub = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Task.FromResult(Auth2Access.Empty);

        if (options.Value.Source == CurrentUserSource.TokenOnly)
            return Task.FromResult(new Auth2Access([.. principal.FindAll("role").Select(c => c.Value).Distinct()], [], [], PvOf(principal), [], []));

        var key = $"auth2:access:{client.Value.ClientId}:{sub}";
        lock (_perRequest)
        {
            if (!_perRequest.TryGetValue(key, out var task)) _perRequest[key] = task = LoadAsync(key, principal, ct);
            return task;
        }
    }

    private async Task<Auth2Access> LoadAsync(string key, ClaimsPrincipal principal, CancellationToken ct)
    {
        var pv = PvOf(principal);
        var cached = await store.GetAsync(key, ct);
        if (cached is not null && cached.AccessVersion >= pv) return cached;   // same or newer than the token: still good

        var me = await users.FetchAsync(principal, ct);
        if (!me.IsSuccess)
        {
            // The server cannot be asked. An old answer is better than none for a short time, but only when the token did not ask for a newer one.
            if (cached is not null && me.Error!.Code == Auth2ClientErrorCodes.ServerUnavailable)
            {
                log.LogWarning("The Auth2 server could not be asked for the user's access. The last known access data is used.");
                return cached;
            }
            throw new Auth2Exception(null, me.Error!.Code, "The access data of the user could not be read from the Auth2 server.", me.Error.Code == Auth2ClientErrorCodes.ServerUnavailable);
        }
        var access = Auth2Access.From(me.Value!);
        await store.SetAsync(key, access, options.Value.CacheTtl, ct);
        _ = clock;   // the store handles the time limit
        return access;
    }

    private static int PvOf(ClaimsPrincipal p) => int.TryParse(p.FindFirst(Auth2Claims.AccessVersion)?.Value, out var v) ? v : 0;
}

internal sealed class CurrentUserImpl(IHttpContextAccessor http, IAuth2AccessProvider access) : ICurrentUser
{
    private ClaimsPrincipal Principal => http.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
    private string? Claim(params string[] types) => types.Select(t => Principal.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;
    public bool IsMachine => Principal.FindFirst(Auth2Claims.Machine)?.Value == "true";
    public Guid? UserId => !IsMachine && Guid.TryParse(Claim("sub", ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? UserName => IsMachine ? null : Claim("preferred_username", "name");
    public string? Email => Claim("email");
    public string? PhoneNumber => Claim("phone_number");
    public string? FirstName => Claim("given_name");
    public string? LastName => Claim("family_name");
    public string? FullName => Claim("name");
    public IReadOnlyList<string> Scopes => ScopeClaims.Read(Principal);

    public async Task<IReadOnlyList<string>> GetRolesAsync(CancellationToken ct = default) => (await access.GetAccessAsync(Principal, ct)).Roles;
    public async Task<IReadOnlyList<string>> GetPermissionsAsync(CancellationToken ct = default) => (await access.GetAccessAsync(Principal, ct)).Permissions;
    public async Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken ct = default) => (await access.GetAccessAsync(Principal, ct)).Groups;
    public async Task<bool> HasPermissionAsync(string permission, CancellationToken ct = default) =>
        (await access.GetAccessAsync(Principal, ct)).Permissions.Contains(permission.Trim().ToLowerInvariant());
    public async Task<bool> HasRoleAsync(string role, CancellationToken ct = default) =>
        (await access.GetAccessAsync(Principal, ct)).Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Reads the scopes of a token (the <c>scope</c> claim can be one string with spaces, or several claims).</summary>
internal static class ScopeClaims
{
    public static IReadOnlyList<string> Read(ClaimsPrincipal principal) =>
        [.. principal.FindAll("scope").Concat(principal.FindAll("scp")).SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal)];
}
