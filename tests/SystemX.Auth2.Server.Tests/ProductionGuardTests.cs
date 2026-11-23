using Microsoft.Extensions.Hosting;
using NSubstitute;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.OpenIddict;

namespace SystemX.Auth2.Server.Tests;

public class ProductionGuardTests
{
    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static Auth2KeyOptions Keys(bool withCertificates = false)
    {
        var o = new Auth2KeyOptions();
        if (withCertificates)
        {
            o.SigningCertificates.Add(new CertificateFileOptions { Path = "sign.pfx" });
            o.EncryptionCertificates.Add(new CertificateFileOptions { Path = "enc.pfx" });
        }
        return o;
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Check_DevelopmentLike_AllowsHttp(string env) =>
        Should.NotThrow(() => ProductionGuard.Check(Env(env), Keys(), new Auth2ServerOptions { Issuer = "http://localhost" }));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Check_NoCertificates_IsFine_TheKeyRingInTheDatabaseIsUsed(string env) =>
        Should.NotThrow(() => ProductionGuard.Check(Env(env), Keys(), new Auth2ServerOptions { Issuer = "https://auth.example.com" }));

    [Fact]
    public void Check_HttpIssuer_Throws() =>
        Should.Throw<InvalidOperationException>(() => ProductionGuard.Check(Env("Production"), Keys(true), new Auth2ServerOptions { Issuer = "http://auth.example.com" }))
            .Message.ShouldContain("https");

    [Fact]
    public void Check_MissingIssuer_Throws() =>
        Should.Throw<InvalidOperationException>(() => ProductionGuard.Check(Env("Production"), Keys(true), new Auth2ServerOptions()));

    [Fact]
    public void Check_CertificatesAndHttpsIssuer_Passes() =>
        Should.NotThrow(() => ProductionGuard.Check(Env("Production"), Keys(true), new Auth2ServerOptions { Issuer = "https://auth.example.com" }));

    [Fact]
    public void KeyOptions_NeedBothSigningAndEncryption()
    {
        var o = new Auth2KeyOptions();
        o.SigningCertificates.Add(new CertificateFileOptions { Path = "sign.pfx" });
        o.HasCertificates.ShouldBeFalse();
    }
}
