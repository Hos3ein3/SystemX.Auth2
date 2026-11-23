using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SystemX.Auth2.Client;
using SystemX.Auth2.Client.DependencyInjection;

namespace SystemX.Auth2.Client.Tests;

/// <summary>A fake server: every request goes to the function and is recorded.</summary>
public sealed class FakeServer : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string Body, string? Authorization)> Calls { get; } = [];
    public Func<HttpRequestMessage, string, Task<HttpResponseMessage>> Respond { get; set; } = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{}"));

    public int CountTo(string path) { lock (Calls) return Calls.Count(c => c.Path == path); }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (Calls) Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body, request.Headers.Authorization?.ToString()));
        return await Respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A normal token answer for <c>/connect/token</c>.</summary>
    public static HttpResponseMessage Token(int expiresIn = 300, string token = "machine-token") =>
        Json(HttpStatusCode.OK, $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{expiresIn}}}""");
}

public static class TestHost
{
    public const string Secret = "super-secret-value-123";

    public static ServiceProvider Build(FakeServer server, Action<IAuth2ClientBuilder>? configure = null, FakeTimeProvider? time = null, Action<Client.Options.Auth2ClientOptions>? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (time is not null) services.AddSingleton<TimeProvider>(time);
        var builder = services.AddAuth2Client("https://auth.test", o =>
        {
            o.ApplicationName = "Blog";
            o.ClientId = "blog";
            o.ClientSecret = Secret;
            o.HandshakeTimeout = TimeSpan.FromSeconds(2);
            options?.Invoke(o);
        });
        configure?.Invoke(builder);
        foreach (var name in new[] { Auth2ClientDefaults.HttpClientName, Auth2ClientDefaults.ResilientHttpClientName })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => server);
        return services.BuildServiceProvider();
    }
}
