using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Client;
using OpenIddict.Server;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>
/// The providers of all projects, as OpenIddict.Client registrations. The registrations are NOT written at start: they are built from the
/// provider list of <see cref="IExternalProviderRegistry"/> each time it changes (spike 1: <c>IConfigureOptions</c> + <c>IOptionsChangeTokenSource</c>),
/// so a provider that an admin adds, changes or removes is used at once on this instance and, through the settings version poll or Redis, on all instances.
/// </summary>
/// <remarks>
/// A registration that cannot be built (a broken secret, a bad authority) is skipped and logged. It never reaches OpenIddict, because OpenIddict
/// throws when it builds its options from a bad registration, and that would stop the sign-in of every other provider.
/// </remarks>
public sealed class ExternalClientRegistrations : IConfigureOptions<OpenIddictClientOptions>, IOptionsChangeTokenSource<OpenIddictClientOptions>, IDisposable
{
    private readonly IExternalProviderRegistry _registry;
    private readonly IExternalSecretProtector _secrets;
    private readonly IOptionsMonitor<OpenIddictServerOptions> _server;
    private readonly IExternalProviderDescriptors _descriptors;
    private readonly IServiceProvider _services;
    private readonly ILogger<ExternalClientRegistrations> _log;
    private readonly ConcurrentDictionary<string, string> _failures = new();
    private CancellationTokenSource _changed = new();

    // A social registration made by the library's builder keeps a reference to the small container that made it (it uses it for discovery).
    // So that container must live as long as the registration can be used. Two generations are kept: the registrations of the options that were
    // replaced a moment ago may still serve a request that is running.
    private readonly object _containers = new();
    private List<IDisposable> _current = [], _previous = [];

    public ExternalClientRegistrations(IExternalProviderRegistry registry, IExternalSecretProtector secrets, IOptionsMonitor<OpenIddictServerOptions> server, IExternalProviderDescriptors descriptors, IServiceProvider services, ILogger<ExternalClientRegistrations> log)
    {
        (_registry, _secrets, _server, _descriptors, _services, _log) = (registry, secrets, server, descriptors, services, log);
        _registry.Changed += OnChanged;
    }

    /// <summary>
    /// The path under which the private callback context of <see cref="OpenIddictIdentitySource"/> runs. It is the only redirection endpoint OpenIddict knows, so the
    /// real callback address of a provider (<c>/callback/login/...</c>) is NOT handled by the OpenIddict middleware: our own endpoint reads that answer.
    /// </summary>
    public const string InternalCallbackPath = "/auth2-internal/external-callback";

    public string? Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_changed.Token);

    private void OnChanged() => Interlocked.Exchange(ref _changed, new CancellationTokenSource()).Cancel();

    public void Dispose() => _registry.Changed -= OnChanged;

    /// <summary>Why a provider has no registration (for the admin "test" and the logs). Null when it has one.</summary>
    public string? FailureOf(string registrationName) => _failures.GetValueOrDefault(registrationName);

    public void Configure(OpenIddictClientOptions options)
    {
        // State tokens and their keys: the same shared keys as the Server's own tokens, so any instance can finish a sign-in that another one started.
        var server = _server.CurrentValue;
        foreach (var c in server.SigningCredentials) options.SigningCredentials.Add(c);
        foreach (var c in server.EncryptionCredentials) options.EncryptionCredentials.Add(c);

        _failures.Clear();
        List<IDisposable> made = [];
        foreach (var p in _registry.ListAll().Where(p => p.Settings is OidcProviderSettings))
        {
            try { options.Registrations.Add(Build(p, made)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failures[p.RegistrationName] = ex.GetType().Name;
                _log.LogWarning("The provider {Provider} has no OpenID Connect registration: {Kind}. It is skipped.", p.RegistrationName, ex.GetType().Name);   // never the message: it may hold a secret
            }
        }

        List<IDisposable> old;
        lock (_containers) { old = _previous; _previous = _current; _current = made; }
        foreach (var d in old) d.Dispose();
    }

    public OpenIddictClientRegistration Build(RegisteredProvider p) => Build(p, null);

    private OpenIddictClientRegistration Build(RegisteredProvider p, List<IDisposable>? made)
    {
        var o = (OidcProviderSettings)p.Settings;
        var redirect = new Uri(ExternalProviderUrls.OidcCallbackPath(p.ProjectSlug, p.Name), UriKind.Relative);
        var secret = _secrets.Unprotect(o.ClientSecretProtected);
        // Without scopes of its own, a provider gets the defaults of its type (the descriptor), for example "name email" for Apple.
        var scopes = o.Scopes.Count > 0 ? o.Scopes : o.Type == ExternalProviderType.GenericOidc ? null : [.. _descriptors.For(o.Type).DefaultScopes];

        if (o.Type == ExternalProviderType.GenericOidc)
        {
            if (!Uri.TryCreate(o.Authority, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps && !AllowHttp) throw new InvalidOperationException("authority");
            var r = new OpenIddictClientRegistration
            {
                Issuer = authority,
                ProviderName = p.RegistrationName,
                ProviderDisplayName = p.Settings.DisplayName ?? p.Name,
                ClientId = o.ClientId,
                ClientSecret = secret,
                RedirectUri = redirect,
                GrantTypes = { GrantTypes.AuthorizationCode },
                ResponseTypes = { ResponseTypes.Code },
            };
            foreach (var s in scopes ?? [Scopes.OpenId, Scopes.Email, Scopes.Profile]) r.Scopes.Add(s);
            // A multi-tenant provider puts the tenant into the issuer of the token. Only the issuers the admin listed (and the authority itself) are accepted.
            if (o.AllowedIssuers.Count > 0) r.TokenValidationParameters.ValidIssuers = [.. o.AllowedIssuers.Append(o.Authority!.TrimEnd('/')).Append(o.Authority!.TrimEnd('/') + "/")];
            return r;
        }

        // The five social providers come from OpenIddict.Client.WebIntegration. A provider type is an internal id there, so the registration is made by the
        // library's own public builder in a small container and copied (spike 1, finding 4).
        var temp = new ServiceCollection();
        temp.AddLogging();
        // The registration keeps using the HTTP client factory of this small container (discovery, keys). So it must have the same protection as the main one:
        // the SSRF guard and, in tests, the fake provider. The standard filters of the HTTP factory are added by the factory itself.
        foreach (var filter in _services.GetServices<Microsoft.Extensions.Http.IHttpMessageHandlerBuilderFilter>().Where(f => f.GetType().Namespace?.StartsWith("Microsoft.Extensions.Http", StringComparison.Ordinal) != true))
            temp.AddSingleton(filter);
        temp.AddOpenIddict().AddClient(c =>
        {
            c.AllowAuthorizationCodeFlow();
            c.DisableTokenStorage();
            c.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
            c.UseAspNetCore();
            c.UseSystemNetHttp();
            var web = c.UseWebProviders();
            switch (o.Type)
            {
                case ExternalProviderType.Google:
                    web.AddGoogle(g => { Common(g, p, o, secret, redirect, scopes); });
                    break;
                case ExternalProviderType.Microsoft:
                    web.AddMicrosoft(m => { Common(m, p, o, secret, redirect, scopes); m.SetTenant(string.IsNullOrWhiteSpace(o.TenantId) ? "common" : o.TenantId); });
                    break;
                case ExternalProviderType.GitHub:
                    web.AddGitHub(g => { Common(g, p, o, secret, redirect, scopes); });
                    break;
                case ExternalProviderType.Facebook:
                    web.AddFacebook(f => { Common(f, p, o, secret, redirect, scopes); f.AddFields("id", "name", "email", "first_name", "last_name", "picture"); });
                    break;
                case ExternalProviderType.Apple:
                    web.AddApple(a =>
                    {
                        Common(a, p, o, null, redirect, scopes);
                        var pem = _secrets.Unprotect(o.PrivateKeyProtected) ?? throw new InvalidOperationException("private key");
                        var ecdsa = ECDsa.Create();
                        ecdsa.ImportFromPem(pem);
                        a.AddResponseModes(ResponseModes.FormPost);   // Apple sends name and email only with a form post
                        a.SetTeamId(o.TeamId ?? throw new InvalidOperationException("team id")).SetSigningKey(new ECDsaSecurityKey(ecdsa) { KeyId = o.KeyId });
                    });
                    break;
                default: throw new InvalidOperationException("type");
            }
        });
        var sp = temp.BuildServiceProvider();
        made?.Add(sp);   // disposed two rebuilds later; a caller without a list (a test) lets the garbage collector do it
        var registration = sp.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue.Registrations.Single();
        // The library fixes the Graph API version of Facebook in its endpoints. The admin may choose another one (for example v19.0).
        if (o.Type == ExternalProviderType.Facebook && !string.IsNullOrWhiteSpace(o.ApiVersion) && registration.Configuration is { } cfg)
        {
            Uri Versioned(Uri? u) => u is null ? null! : new Uri(System.Text.RegularExpressions.Regex.Replace(u.AbsoluteUri, @"/v\d+\.\d+/", $"/{o.ApiVersion}/"));
            cfg.AuthorizationEndpoint = Versioned(cfg.AuthorizationEndpoint);
            cfg.TokenEndpoint = Versioned(cfg.TokenEndpoint);
            if (cfg.UserInfoEndpoint is not null) cfg.UserInfoEndpoint = Versioned(cfg.UserInfoEndpoint);
        }
        return registration;
    }

    private static void Common<T>(T builder, RegisteredProvider p, OidcProviderSettings o, string? secret, Uri redirect, List<string>? scopes) where T : notnull
    {
        // The generated builders have the same fluent methods but no common base type, so the few calls go through dynamic.
        dynamic b = builder;
        b.SetProviderName(p.RegistrationName);
        b.SetProviderDisplayName(p.Settings.DisplayName ?? p.Name);
        b.SetClientId(o.ClientId);
        if (secret is not null) b.SetClientSecret(secret);
        b.SetRedirectUri(redirect);
        foreach (var s in scopes ?? []) b.AddScopes(s);
    }

    /// <summary>Plain HTTP authorities are only for tests and development (the same switch as the private network guard).</summary>
    private bool AllowHttp => false;
}
