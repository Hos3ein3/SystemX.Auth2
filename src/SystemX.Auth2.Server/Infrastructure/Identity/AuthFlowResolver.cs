using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.Identity;

/// <summary>Reads rules of the current application. Scoped.</summary>
public sealed class AuthFlowResolver(IApplicationContext context, IApplicationSettingsProvider settings, IOptions<Auth2ServerOptions> options) : IAuthFlowResolver
{
    /// <inheritdoc />
    public Task<Auth2Application> GetApplicationAsync(CancellationToken ct = default) => context.GetRequiredAsync(ct);

    /// <inheritdoc />
    public async Task<ApplicationSettings> GetSettingsAsync(CancellationToken ct = default) =>
        await settings.GetAsync((await context.GetRequiredAsync(ct)).Id, ct);

    /// <inheritdoc />
    public async Task<CredentialScope> GetCredentialScopeAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        if (o.Mode == ServerMode.SingleApplication) return CredentialScope.Shared;
        if (o.AllowCredentialScopeOverride)
        {
            var s = await GetSettingsAsync(ct);
            if (s.CredentialScope is { } scope) return scope;
        }
        return o.CredentialScope;
    }
}
