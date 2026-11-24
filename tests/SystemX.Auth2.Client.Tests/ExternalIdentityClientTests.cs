using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using SystemX.Auth2.Client.CurrentUser;

namespace SystemX.Auth2.Client.Tests;

public class CurrentUserExternalClaimsTests
{
    private static ICurrentUser UserWith(params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return new CurrentUserImpl(new HttpContextAccessor { HttpContext = context }, Substitute.For<IAuth2AccessProvider>());
    }

    [Fact]
    public void ExternalSignIn_ShowsTheProviderAndTheMethods()
    {
        var user = UserWith(new Claim("sub", Guid.NewGuid().ToString()), new Claim("idp", "corp-saml"), new Claim("amr", "fed"), new Claim("amr", "mfa"), new Claim("amr", "fed"));
        user.IdentityProvider.ShouldBe("corp-saml");
        user.AuthenticationMethods.ShouldBe(["fed", "mfa"]);   // each method once
    }

    [Fact]
    public void LocalSignIn_HasNoProvider_AndMachineTokensHaveNoMethods()
    {
        var local = UserWith(new Claim("sub", Guid.NewGuid().ToString()), new Claim("amr", "pwd"));
        local.IdentityProvider.ShouldBeNull();
        local.AuthenticationMethods.ShouldBe(["pwd"]);
        UserWith(new Claim("sub", "client"), new Claim("machine", "true")).AuthenticationMethods.ShouldBeEmpty();
        UserWith().IdentityProvider.ShouldBeNull();
    }
}
