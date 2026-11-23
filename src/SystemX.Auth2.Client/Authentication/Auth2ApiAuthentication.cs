using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SystemX.Auth2.Client.DependencyInjection;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.Options;

namespace SystemX.Auth2.Client.Options
{

/// <summary>Options of API authentication (<c>UseApiAuthentication</c>): checking the JWT access tokens of the Auth2 Server.</summary>
public sealed class Auth2ApiOptions
{
    /// <summary>The address of the server, used to find the signing keys. Default: <c>ServerUrl</c>. It must be the issuer the server writes in its tokens.</summary>
    public string? Authority { get; set; }

    /// <summary>The audience a token must have. Tokens of the server carry the client id of the application. Default: <c>ClientId</c>.</summary>
    public string? Audience { get; set; }

    /// <summary>Insist on https for the key address. Default: true, except when the server address is http (development).</summary>
    public bool? RequireHttpsMetadata { get; set; }

    /// <summary>Allowed difference between clocks. Default 30 seconds.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The shortest time between two reads of the server's keys. When a token with an unknown key id arrives (after a key rotation) the keys are read again,
    /// but not more often than this. Default 30 seconds.
    /// </summary>
    public TimeSpan KeyRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Handler for the calls that get the keys (tests, proxies). Default: none.</summary>
    public HttpMessageHandler? BackchannelHandler { get; set; }
}

internal sealed class RefreshGate(TimeSpan interval)
{
    private long _next;

    /// <summary>True for the caller that may start a refresh now; false while the last one is too recent.</summary>
    public bool TryEnter()
    {
        var now = Environment.TickCount64;
        var next = Interlocked.Read(ref _next);
        return now >= next && Interlocked.CompareExchange(ref _next, now + (long)interval.TotalMilliseconds, next) == next;
    }
}

internal sealed class Auth2JwtBearerConfigurator(IOptions<Auth2ClientOptions> client, IOptions<Auth2ApiOptions> api) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;
        var c = client.Value;
        var a = api.Value;
        options.Authority = (a.Authority ?? c.ServerUrl).TrimEnd('/');
        options.Audience = a.Audience ?? c.ClientId;
        options.RequireHttpsMetadata = a.RequireHttpsMetadata ?? c.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        options.MapInboundClaims = false;   // keep "sub", "role", "scope" as the server writes them
        options.BackchannelHttpHandler = a.BackchannelHandler;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = a.ClockSkew,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
        // The keys come from the discovery document and are cached. A token with an unknown key id makes the handler read the keys again (key rotation).
        options.RefreshOnIssuerKeyNotFound = true;
        options.RefreshInterval = a.KeyRefreshInterval;

        // After a key rotation the first token with the new key must not be refused: when its key id is unknown, the keys are read again and
        // we wait (up to 2 seconds) for them. This happens at most once per KeyRefreshInterval, so tokens with made-up key ids cannot make the server wait.
        var gate = new RefreshGate(a.KeyRefreshInterval);
        options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) =>
        {
            var manager = options.ConfigurationManager!;
            IEnumerable<SecurityKey> Keys() => manager.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult().SigningKeys;
            var keys = Keys();
            if (string.IsNullOrEmpty(kid) || keys.Any(k => k.KeyId == kid) || !gate.TryEnter()) return keys;
            manager.RequestRefresh();
            for (var i = 0; i < 20; i++)
            {
                Thread.Sleep(100);
                keys = Keys();
                if (keys.Any(k => k.KeyId == kid)) break;
            }
            return keys;
        };
    }
}

}

namespace Microsoft.Extensions.DependencyInjection
{
/// <summary>Authentication features of the builder.</summary>
public static class Auth2ApiAuthenticationExtensions
{
    /// <summary>
    /// Accepts the JWT access tokens of the Auth2 Server (for APIs). The keys are found through the server's discovery document, cached, and read again
    /// when a token with a new key id arrives (so key rotation needs no restart). Machine tokens and user tokens are both accepted; limit them per endpoint with
    /// <c>WithUserOnly()</c> / <c>WithMachineOnly()</c>.
    /// </summary>
    public static IAuth2ClientBuilder UseApiAuthentication(this IAuth2ClientBuilder builder, Action<Auth2ApiOptions>? configure = null)
    {
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddOptions<Auth2ApiOptions>();
        builder.Services.AddOptions<Auth2AuthFlags>().Configure(f => f.HasBearer = true);
        builder.Services.AddAuthentication(o =>
        {
            o.DefaultScheme ??= JwtBearerDefaults.AuthenticationScheme;
            o.DefaultAuthenticateScheme ??= JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme ??= JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });
        builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, Auth2JwtBearerConfigurator>();
        return builder;
    }
}
}
