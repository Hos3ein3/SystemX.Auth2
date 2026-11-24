using Microsoft.AspNetCore.Identity;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Infrastructure.Operations;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>
/// The password login with a directory in the picture. It wraps <see cref="LoginService"/>: when the application has an enabled LDAP provider, its <see cref="LdapMode"/>
/// decides the order of the local password and the directory. Everything else is passed on unchanged. The result of a directory login goes through the same
/// decision table and the same login pipeline as every other external sign-in.
/// </summary>
internal sealed class LdapAwareLoginService(
    LoginService inner, IExternalProviderRegistry registry, ILdapDirectory directory, IServiceProvider services, IAuthFlowResolver flow,
    IRateLimiter limiter, UserManager<AppUser> users, IAuditService audit, Microsoft.Extensions.Options.IOptions<Auth2ServerOptions> options) : ILoginService
{
    /// <inheritdoc />
    public async Task<LoginResult> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default)
    {
        var app = await flow.GetApplicationAsync(ct);
        var providers = registry.ListForApplication(app.Id).Where(p => p.Type == ExternalProviderType.Ldap).OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        if (providers.Count == 0) return await inner.PasswordLoginAsync(request, ct);

        var mode = ((LdapProviderSettings)providers[0].Settings).Mode;
        switch (mode)
        {
            case LdapMode.LdapOnly:
                return await DirectoryAsync(app, providers, request, ct);
            case LdapMode.LdapThenLocal:
            {
                var first = await DirectoryAsync(app, providers, request, ct);
                return first.Succeeded || first.ErrorCode != Auth2ErrorCodes.InvalidCredentials ? first : await inner.PasswordLoginAsync(request, ct);
            }
            default:   // LocalThenLdap
            {
                var local = await inner.PasswordLoginAsync(request, ct);
                return local.Succeeded || local.ErrorCode != Auth2ErrorCodes.InvalidCredentials ? local : await DirectoryAsync(app, providers, request, ct);
            }
        }
    }

    private async Task<LoginResult> DirectoryAsync(Auth2Application app, List<RegisteredProvider> providers, PasswordLoginRequest request, CancellationToken ct)
    {
        // Rate limit before any call to the directory, per user and IP (policy "ldap").
        var rule = options.Value.RateLimiting.RuleFor("ldap");
        var decision = await limiter.HitAsync($"ldap:{app.Id:N}:{request.Login.Trim().ToLowerInvariant()}:{request.IpAddress}", rule.PermitLimit, TimeSpan.FromSeconds(rule.WindowSeconds), ct);
        if (!decision.Allowed) return LoginResult.Fail(Auth2ErrorCodes.RateLimited);

        LoginResult last = LoginResult.Fail(Auth2ErrorCodes.InvalidCredentials);
        foreach (var provider in providers)
        {
            try
            {
                var identity = await directory.AuthenticateAsync(provider, request.Login, request.Password, ct);
                var external = services.GetRequiredService<IExternalLoginService>();
                var outcome = await external.HandleAsync(new ExternalLoginRequest(provider, identity, "/", request.IpAddress, request.UserAgent, IsDirectoryLogin: true), ct);
                return outcome.Login ?? LoginResult.Fail(outcome.ErrorCode ?? Auth2ErrorCodes.ExternalClaimsInvalid);
            }
            catch (LdapAuthException ex)
            {
                await audit.WriteAsync(new AuditEntry("ldap.login.failed", app.Id, IpAddress: request.IpAddress, UserAgent: request.UserAgent,
                    Data: new Dictionary<string, object?> { ["provider"] = provider.Name, ["code"] = ex.ErrorCode, ["reason"] = ex.Reason }), ct);   // never the user name or the password
                // A wrong password for a known directory user counts toward the lockout of the linked local user.
                if (ex.Subject is not null && await users.FindByLoginAsync(provider.Name, ex.Subject) is { } linked) await users.AccessFailedAsync(linked);
                last = LoginResult.Fail(ex.ErrorCode == Auth2ErrorCodes.LdapUnavailable ? Auth2ErrorCodes.LdapUnavailable : ex.ErrorCode == Auth2ErrorCodes.LdapAccountDisabled ? ex.ErrorCode : Auth2ErrorCodes.InvalidCredentials);
                if (ex.ErrorCode == Auth2ErrorCodes.LdapUnavailable && provider != providers[^1]) continue;
            }
        }
        return last;
    }

    // ---- everything else is passed on
    public Task<LoginResult> OtpLoginAsync(OtpLoginRequest request, CancellationToken ct = default) => inner.OtpLoginAsync(request, ct);
    public Task<LoginResult> ExternalLoginAsync(ExternalSignInRequest request, CancellationToken ct = default) => inner.ExternalLoginAsync(request, ct);
    public Task<LoginResult> CompleteTwoFactorAsync(string twoFactorToken, string? code, string? recoveryCode, string? ipAddress = null, CancellationToken ct = default) =>
        inner.CompleteTwoFactorAsync(twoFactorToken, code, recoveryCode, ipAddress, ct);
    public Task<bool> IsSubjectValidAsync(AppUser user, CancellationToken ct = default) => inner.IsSubjectValidAsync(user, ct);
}
