using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>A tiny SMTP server in memory: it accepts mail and keeps the text it received.</summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public FakeSmtpServer()
    {
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Everything received in DATA, one entry per mail, with the envelope recipients first.</summary>
    public ConcurrentQueue<string> Mails { get; } = new();

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => TalkAsync(client));
            }
        }
        catch { /* stopped */ }
    }

    private async Task TalkAsync(TcpClient client)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 fake.smtp ready");
        var rcpt = new List<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            var cmd = line.ToUpperInvariant();
            if (cmd.StartsWith("EHLO") || cmd.StartsWith("HELO")) await writer.WriteLineAsync("250 fake.smtp");
            else if (cmd.StartsWith("MAIL FROM")) await writer.WriteLineAsync("250 ok");
            else if (cmd.StartsWith("RCPT TO")) { rcpt.Add(line); await writer.WriteLineAsync("250 ok"); }
            else if (cmd == "DATA")
            {
                await writer.WriteLineAsync("354 go on");
                var body = new StringBuilder(string.Join("\n", rcpt) + "\n");
                while (await reader.ReadLineAsync() is { } l && l != ".") body.AppendLine(l);
                Mails.Enqueue(body.ToString());
                await writer.WriteLineAsync("250 queued");
            }
            else if (cmd == "QUIT") { await writer.WriteLineAsync("221 bye"); return; }
            else await writer.WriteLineAsync("250 ok");
        }
    }

    public void Dispose() { _stop.Cancel(); _listener.Stop(); }
}

/// <summary>Email goes through the factory: the application's provider, else the project's, else the server default.</summary>
public class MessagingTests : AdminBase, IDisposable
{
    private readonly FakeSmtpServer _smtpA = new();
    private readonly FakeSmtpServer _smtpB = new();

    /// <summary>The real path: <see cref="IEmailSender"/> asks the factory (the test host would otherwise capture every message).</summary>
    protected override void Customize(IServiceCollection services) =>
        services.AddScoped<IEmailSender, FactoryBackedSender>();

    private sealed class FactoryBackedSender(IEmailFactory factory) : IEmailSender
    {
        public async Task SendAsync(EmailMessage message, CancellationToken ct = default) => await (await factory.CreateAsync(ct)).SendAsync(message, ct);
    }

    private object SmtpTo(FakeSmtpServer server, string from) =>
        new { email = new { provider = "smtp", host = "127.0.0.1", port = server.Port, security = "None", fromAddress = from, fromName = "Acme Mail" } };

    private async Task<Guid> ApplicationIdAsync(string clientId)
    {
        await using var scope = NewScope();
        return (await scope.ServiceProvider.GetRequiredService<IApplicationService>().FindByClientIdAsync(clientId))!.Id;
    }

    private async Task RegisterAsync(string clientId, string userName, string email)
    {
        var token = await ClientTokenAsync(clientId, "auth2.register");
        (await CallAsync(new Browser(Factory), Auth2Routes.Register, new RegisterRequest(userName, email, null, "Very-Secret-Pass-1!"), token)).Status.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<string> WaitForMailAsync(FakeSmtpServer server)
    {
        for (var i = 0; i < 100 && server.Mails.IsEmpty; i++) await Task.Delay(50);
        server.Mails.TryDequeue(out var mail).ShouldBeTrue("no mail arrived at the SMTP server");
        return mail!;
    }

    [Fact]
    public async Task TheProjectProvider_SendsTheRegistrationEmailThroughSmtp()
    {
        await StartAsync();
        var put = await SendAsync(HttpMethod.Put, "/messaging", GlobalToken, SmtpTo(_smtpA, "noreply@acme.test"), ("X-Auth2-Application", "appa"));
        put.Status.ShouldBe(HttpStatusCode.NoContent, put.Body.ToString());

        await RegisterAsync("appa", "sara", "sara@example.com");
        var mail = await WaitForMailAsync(_smtpA);
        mail.ShouldContain("sara@example.com");
        mail.ShouldContain("noreply@acme.test");
        mail.ShouldContain("/Account/ConfirmEmail");   // the confirmation link
    }

    [Fact]
    public async Task AnApplicationProvider_WinsOverTheProjectProvider_AndClearingItGoesBack()
    {
        await StartAsync();
        (await SendAsync(HttpMethod.Put, "/messaging", GlobalToken, SmtpTo(_smtpA, "project@acme.test"), ("X-Auth2-Application", "appa"))).Status.ShouldBe(HttpStatusCode.NoContent);
        var appId = await ApplicationIdAsync("appa");
        var own = await SendAsync(HttpMethod.Put, $"/projects/{A.Id}/applications/{appId}/messaging", GlobalToken, SmtpTo(_smtpB, "app@acme.test"));
        own.Status.ShouldBe(HttpStatusCode.NoContent, own.Body.ToString());

        await RegisterAsync("appa", "sara", "sara@example.com");
        (await WaitForMailAsync(_smtpB)).ShouldContain("app@acme.test");
        _smtpA.Mails.ShouldBeEmpty();

        (await SendAsync(HttpMethod.Delete, $"/projects/{A.Id}/applications/{appId}/messaging", GlobalToken)).Status.ShouldBe(HttpStatusCode.NoContent);
        await RegisterAsync("appa", "tom", "tom@example.com");
        (await WaitForMailAsync(_smtpA)).ShouldContain("project@acme.test");
    }

    [Fact]
    public async Task WithoutAnySetting_TheServerDefaultIsUsed_ItLogsAndSendsNothing()
    {
        await StartAsync();
        await RegisterAsync("appa", "sara", "sara@example.com");
        _smtpA.Mails.ShouldBeEmpty();
        Factory.Logs.Entries.Any(e => e.Message.Contains("Email to s***@example.com")).ShouldBeTrue();
    }

    [Fact]
    public async Task ThePassword_IsEncrypted_NeverReturned_AndKeptWhenNotSent()
    {
        await StartAsync();
        var set = await SendAsync(HttpMethod.Put, "/messaging", GlobalToken,
            new { email = new { provider = "google", userName = "me@gmail.com", password = "abcd efgh ijkl mnop", fromName = "Acme" } }, ("X-Auth2-Application", "appa"));
        set.Status.ShouldBe(HttpStatusCode.NoContent, set.Body.ToString());

        var read = await GetA("/messaging", GlobalToken, ("X-Auth2-Application", "appa"));
        read.Status.ShouldBe(HttpStatusCode.OK);
        read.Body.GetRawText().ShouldNotContain("abcd efgh");
        read.Body.GetProperty("email").GetProperty("passwordIsSet").GetBoolean().ShouldBeTrue();

        // Another change without a password keeps it; the generic settings route never touches it.
        (await SendAsync(HttpMethod.Put, "/messaging", GlobalToken, new { email = new { fromName = "Acme 2" } }, ("X-Auth2-Application", "appa"))).Status.ShouldBe(HttpStatusCode.NoContent);
        var settings = await GetA($"/applications/{A.Id}/settings", GlobalToken);
        settings.Body.GetRawText().ShouldNotContain("abcd efgh");
        (await SendAsync(HttpMethod.Put, $"/applications/{A.Id}/settings", GlobalToken, settings.Body.GetRawText())).Status.ShouldBe(HttpStatusCode.NoContent);
        var after = await GetA("/messaging", GlobalToken, ("X-Auth2-Application", "appa"));
        after.Body.GetProperty("email").GetProperty("passwordIsSet").GetBoolean().ShouldBeTrue();
        after.Body.GetProperty("email").GetProperty("provider").GetString().ShouldBe("google");

        // An empty text removes it.
        (await SendAsync(HttpMethod.Put, "/messaging", GlobalToken, new { email = new { provider = "logging", password = "" } }, ("X-Auth2-Application", "appa"))).Status.ShouldBe(HttpStatusCode.NoContent);
        (await GetA("/messaging", GlobalToken, ("X-Auth2-Application", "appa"))).Body.GetProperty("email").GetProperty("passwordIsSet").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task WrongSettings_AreRejected_AndOnlyAGlobalAdminMayChangeThem()
    {
        await StartAsync();
        foreach (var bad in new object[]
        {
            new { email = new { provider = "carrier-pigeon" } },
            new { email = new { provider = "smtp", fromAddress = "a@b.test" } },                                  // no host
            new { email = new { provider = "smtp", host = "https://evil.test/x", fromAddress = "a@b.test" } },   // not a host name
            new { email = new { provider = "smtp", host = "mail.test", fromAddress = "not an address" } },
            new { email = new { provider = "smtp", host = "mail.test", fromAddress = "a@b.test", userName = "u" } },   // user without password
            new { email = new { provider = "google", userName = "me@gmail.com" } },                              // no app password
            new { email = new { port = 70000 } },
            new { sms = new { provider = "nope" } },
        })
        {
            var r = await SendAsync(HttpMethod.Put, "/messaging", GlobalToken, bad, ("X-Auth2-Application", "appa"));
            r.Status.ShouldBe(HttpStatusCode.BadRequest, System.Text.Json.JsonSerializer.Serialize(bad));
        }
        (await SendAsync(HttpMethod.Put, "/messaging", ProjectToken, new { email = new { provider = "logging" } }, ("X-Auth2-Application", "appa"))).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await GetA("/messaging", ProjectToken, ("X-Auth2-Application", "appa"))).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    public void Dispose()
    {
        _smtpA.Dispose();
        _smtpB.Dispose();
    }
}
