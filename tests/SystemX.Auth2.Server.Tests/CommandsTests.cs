using SystemX.Auth2.Server.Infrastructure.Commands;

namespace SystemX.Auth2.Server.Tests;

public class CommandsTests
{
    [Theory]
    [InlineData("migrate")]
    [InlineData("seed")]
    [InlineData("create-project")]          // was missing: the server started as a web server instead of running the command
    [InlineData("create-application")]
    [InlineData("rotate-client-secret")]
    [InlineData("rotate-keys")]
    [InlineData("create-admin")]
    public void EveryDocumentedCommand_IsRecognised(string command) => Auth2Commands.IsCommand([command]).ShouldBeTrue();

    [Theory]
    [InlineData("convert-to-single")]       // not built yet (docs/roadmap.md)
    [InlineData("--urls")]
    public void AnythingElse_StartsTheWebServer(string argument) => Auth2Commands.IsCommand([argument]).ShouldBeFalse();

    [Fact]
    public void NoArguments_StartsTheWebServer() => Auth2Commands.IsCommand([]).ShouldBeFalse();
}
