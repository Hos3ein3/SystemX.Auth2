using SystemX.Auth2.Server.Infrastructure.Auth;

namespace SystemX.Auth2.Server.Tests;

public class AccessKeysTests
{
    [Theory]
    [InlineData("orders.read", "orders.read")]
    [InlineData("  Orders.READ ", "orders.read")]
    [InlineData("a", "a")]
    [InlineData("blog:posts-edit_2", "blog:posts-edit_2")]
    public void Valid_KeysAreTrimmedAndLowercased(string input, string expected) =>
        AccessKeys.NormalizePermissionKey(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData(".starts.with.dot")]
    [InlineData("ends.with.dash-")]
    [InlineData("slash/not/allowed")]
    [InlineData("ünïcode")]
    public void Invalid_KeysAreRejected(string? input) => AccessKeys.NormalizePermissionKey(input).ShouldBeNull();

    [Fact]
    public void Length_IsLimitedTo100()
    {
        AccessKeys.NormalizePermissionKey(new string('a', 100)).ShouldNotBeNull();
        AccessKeys.NormalizePermissionKey(new string('a', 101)).ShouldBeNull();
    }
}
