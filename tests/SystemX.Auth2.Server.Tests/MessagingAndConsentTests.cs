using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Tests;

public class ConsentPolicyTests
{
    private static ProjectSettings Settings(bool require, ConsentMode mode)
    {
        var s = new ProjectSettings();
        s.Sessions.RequireConsent = require;
        s.Sessions.ConsentMode = mode;
        return s;
    }

    [Theory]
    [InlineData(false, ConsentMode.Implicit, ConsentMode.Implicit)]
    [InlineData(false, ConsentMode.Explicit, ConsentMode.Implicit)]    // RequireConsent off: never ask
    [InlineData(false, ConsentMode.Systematic, ConsentMode.Implicit)]
    [InlineData(true, ConsentMode.Implicit, ConsentMode.Explicit)]     // RequireConsent on and no mode chosen: ask once
    [InlineData(true, ConsentMode.Explicit, ConsentMode.Explicit)]
    [InlineData(true, ConsentMode.Systematic, ConsentMode.Systematic)]
    public void Effective_Cases(bool require, ConsentMode mode, ConsentMode expected) =>
        ConsentPolicy.Effective(Settings(require, mode)).ShouldBe(expected);
}

public class MaskTests
{
    [Theory]
    [InlineData("sara@example.com", "s***@example.com")]
    [InlineData("a@b.c", "a***@b.c")]
    [InlineData("nonsense", "***")]
    [InlineData("@x.com", "***")]
    public void Email_HidesTheName(string input, string expected) => Mask.Email(input).ShouldBe(expected);

    [Theory]
    [InlineData("+31612345678", "+31*******78")]
    [InlineData("12345", "***")]
    public void Phone_HidesTheMiddle(string input, string expected) => Mask.Phone(input).ShouldBe(expected);
}

public class LoggingSenderTests
{
    private sealed class Capture : ILogger<LoggingEmailSender>, ILogger<LoggingSmsSender>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    private static IHostEnvironment Env(string name)
    {
        var e = Substitute.For<IHostEnvironment>();
        e.EnvironmentName.Returns(name);
        return e;
    }

    private static IOptions<Auth2ServerOptions> Options(bool bodies) => Microsoft.Extensions.Options.Options.Create(new Auth2ServerOptions { Development = { LogMessageBodies = bodies } });

    [Fact]
    public async Task Email_ByDefault_LogsOnlyAMaskedRecipientAndTheSubject()
    {
        var log = new Capture();
        await new LoggingEmailSender(log, Env("Development"), Options(false)).SendAsync(new EmailMessage("sara@example.com", "Hello", "secret-link-and-token"));
        log.Lines.ShouldHaveSingleItem().ShouldBe("Email to s***@example.com: Hello");
    }

    [Fact]
    public async Task Sms_ByDefault_NeverLogsTheText()
    {
        var log = new Capture();
        await new LoggingSmsSender(log, Env("Development"), Options(false)).SendAsync(new SmsMessage("+31612345678", "your code is 123456"));
        log.Lines.ShouldHaveSingleItem().ShouldNotContain("123456");
    }

    [Fact]
    public async Task Bodies_AreLoggedOnlyInDevelopment_AndOnlyWhenAskedFor()
    {
        var dev = new Capture();
        await new LoggingSmsSender(dev, Env("Development"), Options(true)).SendAsync(new SmsMessage("+31612345678", "your code is 123456"));
        dev.Lines.ShouldHaveSingleItem().ShouldContain("123456");

        foreach (var name in new[] { "Production", "Staging", "Testing" })
        {
            var other = new Capture();
            await new LoggingEmailSender(other, Env(name), Options(true)).SendAsync(new EmailMessage("sara@example.com", "Hi", "link-with-token"));
            other.Lines.ShouldAllBe(l => !l.Contains("link-with-token"), name);
        }
    }
}
