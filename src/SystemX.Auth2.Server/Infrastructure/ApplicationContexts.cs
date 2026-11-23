using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure;

/// <summary>Single-application mode: always the one fixed application row (not <c>_global</c>).</summary>
public sealed class SingleApplicationContext(Auth2DbContext db, IApplicationScope scope, IOptions<Auth2ServerOptions> options) : IApplicationContext
{
    private Auth2Application? _cached;

    /// <inheritdoc />
    public async Task<Auth2Application> GetRequiredAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var name = options.Value.SingleApplication.Name;
        var app = await db.Applications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectKey != ProjectKeyRules.GlobalProjectKey && a.Name == name, ct)
            ?? throw new ApplicationNotFoundException($"The single application '{name}' does not exist. Run the 'seed' command.");
        scope.Set(app.Id, null);
        return _cached = app;
    }
}

/// <summary>Multi-application mode: from the admin's selected project, else from the client id of the request.</summary>
public sealed class MultiApplicationContext(Auth2DbContext db, IApplicationScope scope, IClientIdAccessor clientId, IApplicationSelection selection) : IApplicationContext
{
    private Auth2Application? _cached;

    /// <inheritdoc />
    public async Task<Auth2Application> GetRequiredAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        Auth2Application? app;
        if (!string.IsNullOrEmpty(selection.ProjectKey))
        {
            var key = selection.ProjectKey;
            app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectKey == key, ct);
        }
        else if (!string.IsNullOrEmpty(clientId.ClientId))
        {
            var id = clientId.ClientId;
            app = await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == id, ct);
        }
        else
        {
            throw new ApplicationNotFoundException("No client id or project selection is available for this request.");
        }

        if (app is null) throw new ApplicationNotFoundException("No application matches this request.");
        scope.Set(app.Id, app.ProjectKey);
        return _cached = app;
    }
}

/// <summary>Chooses the strategy at runtime from <see cref="Auth2ServerOptions.Mode"/>. Used until <c>ServerWithSingleApplication()</c> replaces it.</summary>
internal sealed class ModeApplicationContext(IOptions<Auth2ServerOptions> options, IServiceProvider sp) : IApplicationContext
{
    private IApplicationContext? _inner;

    public Task<Auth2Application> GetRequiredAsync(CancellationToken ct = default)
    {
        _inner ??= options.Value.Mode == SystemX.Auth2.Contracts.Enums.ServerMode.SingleApplication
            ? ActivatorUtilities.CreateInstance<SingleApplicationContext>(sp)
            : ActivatorUtilities.CreateInstance<MultiApplicationContext>(sp);
        return _inner.GetRequiredAsync(ct);
    }
}
