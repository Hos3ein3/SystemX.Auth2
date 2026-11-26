using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using SystemX.Auth2.Client.CurrentUser;

namespace SystemX.Auth2.Client.Tests;

public class CurrentClientTests
{
    private static ICurrentClient ClientWith(params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return new CurrentClientImpl(new HttpContextAccessor { HttpContext = context });
    }

    private static ICurrentUser UserWith(params Claim[] claims)
    {
        var identity = claims.Length > 0 ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new CurrentUserImpl(new HttpContextAccessor { HttpContext = context }, Substitute.For<IAuth2AccessProvider>());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void TheConfirmedFlags_AreReadFromTheClaims(string value, bool expected)
    {
        var user = UserWith(new Claim("sub", Guid.NewGuid().ToString()), new Claim("email_verified", value), new Claim("phone_number_verified", value));
        user.IsEmailAddressConfirmed.ShouldBe(expected);
        user.IsPhoneNumberConfirmed.ShouldBe(expected);
    }

    [Fact]
    public void TheConfirmedFlags_AreFalse_WithoutTheClaims_AndForAMachineToken()
    {
        var person = UserWith(new Claim("sub", Guid.NewGuid().ToString()));
        (person.IsEmailAddressConfirmed, person.IsPhoneNumberConfirmed).ShouldBe((false, false));   // the token has no email/phone scope: it does not say

        var machine = UserWith(new Claim("sub", "client"), new Claim("machine", "true"), new Claim("email_verified", "true"), new Claim("phone_number_verified", "true"));
        (machine.IsEmailAddressConfirmed, machine.IsPhoneNumberConfirmed).ShouldBe((false, false));
        UserWith().IsEmailAddressConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task HasGroupAsync_IgnoresCase_AndTrims()
    {
        var access = Substitute.For<IAuth2AccessProvider>();
        access.GetAccessAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(new Auth2Access(["Editor"], ["a.b"], ["newsroom"], 1, [], []));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test")) };
        var user = new CurrentUserImpl(new HttpContextAccessor { HttpContext = context }, access);
        (await user.HasGroupAsync("newsroom")).ShouldBeTrue();
        (await user.HasGroupAsync("  NewsRoom ")).ShouldBeTrue();
        (await user.HasGroupAsync("finance")).ShouldBeFalse();
    }

    [Fact]
    public void ExternalSignIn_ShowsTheProviderAndTheMethods()
    {
        var client = ClientWith(new Claim("sub", Guid.NewGuid().ToString()), new Claim("idp", "corp-saml"), new Claim("amr", "fed"), new Claim("amr", "mfa"), new Claim("amr", "fed"));
        client.Idp.ShouldBe("corp-saml");
        client.Amr.ShouldBe(["fed", "mfa"]);   // each method once
    }

    [Fact]
    public void LocalSignIn_HasNoProvider_AndMachineTokensHaveNoMethods()
    {
        var local = ClientWith(new Claim("sub", Guid.NewGuid().ToString()), new Claim("amr", "pwd"));
        local.Idp.ShouldBeNull();
        local.Amr.ShouldBe(["pwd"]);
        ClientWith(new Claim("sub", "client"), new Claim("machine", "true")).Amr.ShouldBeEmpty();
        ClientWith().Idp.ShouldBeNull();
    }

    [Fact]
    public void TheClient_ComesFromTheClaims_WithoutACallToTheServer()
    {
        var app = Guid.NewGuid();
        var project = Guid.NewGuid();
        var c = ClientWith(new Claim("client_id", "papertrade.api"), new Claim("application_id", app.ToString()), new Claim("project_id", project.ToString()),
            new Claim("projectkey", "papertrade"), new Claim("scope", "openid auth2.me"), new Claim("sid", "s1"));
        c.IsTokenAuthenticated.ShouldBeTrue();
        c.ClientId.ShouldBe("papertrade.api");
        c.ApplicationId.ShouldBe(app);
        c.ProjectId.ShouldBe(project);
        c.ProjectKey.ShouldBe("papertrade");
        c.SessionId.ShouldBe("s1");
        c.Scopes.ShouldBe(["openid", "auth2.me"]);
        c.HasScope("auth2.me").ShouldBeTrue();
        c.HasScope("auth2.admin").ShouldBeFalse();
        c.IsMachine.ShouldBeFalse();
    }

    [Fact]
    public void NoToken_MeansNoClientAndNoUser()
    {
        var c = new CurrentClientImpl(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        c.IsTokenAuthenticated.ShouldBeFalse();
        c.ClientId.ShouldBeNull();
        c.ApplicationId.ShouldBeNull();
        c.Scopes.ShouldBeEmpty();
        new CurrentUserImpl(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, Substitute.For<IAuth2AccessProvider>()).IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public void MachineToken_IsATokenAndAMachine_ButNotAUser()
    {
        var claims = new[] { new Claim("sub", "papertrade.api"), new Claim("machine", "true"), new Claim("client_id", "papertrade.api"), new Claim("scope", "orders.sync") };
        var client = ClientWith(claims);
        client.IsTokenAuthenticated.ShouldBeTrue();
        client.IsMachine.ShouldBeTrue();
        client.HasScope("orders.sync").ShouldBeTrue();
        var user = UserWith(claims);
        user.IsAuthenticated.ShouldBeFalse();   // a machine is not a person
        user.UserId.ShouldBeNull();
        user.UserName.ShouldBeNull();
    }

    [Fact]
    public void UserToken_IsAPerson_AndHasNoMachineFlag()
    {
        var id = Guid.NewGuid();
        var claims = new[] { new Claim("sub", id.ToString()), new Claim("preferred_username", "sara"), new Claim("scope", "openid") };
        ClientWith(claims).IsMachine.ShouldBeFalse();
        var user = UserWith(claims);
        user.IsAuthenticated.ShouldBeTrue();
        user.UserId.ShouldBe(id);
        user.UserName.ShouldBe("sara");
    }
}
