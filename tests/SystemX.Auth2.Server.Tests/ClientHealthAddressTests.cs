using System.Net;
using SystemX.Auth2.Server.Infrastructure.Operations;

namespace SystemX.Auth2.Server.Tests;

public class ClientHealthAddressTests
{
    [Theory]
    [InlineData("10.1.2.3", 5001, "http", "/auth2/health", "http://10.1.2.3:5001/auth2/health")]
    [InlineData("127.0.0.1", 5101, "http", null, "http://127.0.0.1:5101/auth2/health")]
    [InlineData("::1", 5101, "http", "/ops/ready", "http://[::1]:5101/ops/ready")]
    [InlineData("::ffff:172.18.0.5", 80, "http", "/auth2/health", "http://172.18.0.5:80/auth2/health")]
    [InlineData("fe80::1%3", 8080, "https", "/h", "https://[fe80::1]:8080/h")]
    public void ADerivedAddress_HasTheHostOfTheConnectionAndThePortOfTheClient(string ip, int port, string scheme, string? path, string expected) =>
        ClientHealthAddress.Derive(IPAddress.Parse(ip), port, scheme, path).ShouldBe(expected);

    [Theory]
    [InlineData(null, 5001, "http", "/h")]
    [InlineData("10.0.0.1", null, "http", "/h")]
    [InlineData("10.0.0.1", 0, "http", "/h")]
    [InlineData("10.0.0.1", 65536, "http", "/h")]
    [InlineData("10.0.0.1", 5001, "gopher", "/h")]
    [InlineData("10.0.0.1", 5001, null, "/h")]
    [InlineData("10.0.0.1", 5001, "http", "h")]
    [InlineData("10.0.0.1", 5001, "http", "/h?x=1")]
    [InlineData("10.0.0.1", 5001, "http", "/h#x")]
    [InlineData("10.0.0.1", 5001, "http", "/a b")]
    [InlineData("10.0.0.1", 5001, "http", "/a\\b")]
    [InlineData("0.0.0.0", 5001, "http", "/h")]
    [InlineData("::", 5001, "http", "/h")]
    public void ADerivedAddress_IsRefused_WhenSomethingIsMissingOrWrong(string? ip, int? port, string? scheme, string path) =>
        ClientHealthAddress.Derive(ip is null ? null : IPAddress.Parse(ip), port, scheme, path).ShouldBeNull();

    [Fact]
    public void ATooLongPath_IsRefused() =>
        ClientHealthAddress.Derive(IPAddress.Loopback, 80, "http", "/" + new string('a', 250)).ShouldBeNull();
}
