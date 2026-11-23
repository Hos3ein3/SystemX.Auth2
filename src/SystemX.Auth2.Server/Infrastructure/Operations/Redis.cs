using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Infrastructure.Operations;

/// <summary>One shared Redis connection (only registered when <c>Auth2:Redis:ConnectionString</c> is set). Connects on first use.</summary>
public sealed class RedisConnection(IOptions<Auth2ServerOptions> options) : IDisposable
{
    private readonly Lazy<ConnectionMultiplexer> _mux = new(() =>
    {
        var config = ConfigurationOptions.Parse(options.Value.Redis.ConnectionString!);
        config.AbortOnConnectFail = false;   // the server still starts when Redis is down; the readiness check shows it
        return ConnectionMultiplexer.Connect(config);
    });

    /// <summary>The multiplexer.</summary>
    public IConnectionMultiplexer Multiplexer => _mux.Value;

    /// <summary>The database.</summary>
    public IDatabase Database => _mux.Value.GetDatabase();

    /// <summary>Key prefix.</summary>
    public string Prefix => options.Value.Redis.KeyPrefix;

    /// <summary>Builds a key or channel name with the prefix.</summary>
    public string Key(string name) => $"{Prefix}:{name}";

    /// <inheritdoc />
    public void Dispose() { if (_mux.IsValueCreated) _mux.Value.Dispose(); }
}
