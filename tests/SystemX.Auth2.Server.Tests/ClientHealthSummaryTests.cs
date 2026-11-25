using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Tests;

public class ClientHealthSummaryTests
{
    private static ClientHealthInfo Client(string name, ClientHealthState state, string? message = null) =>
        new(Guid.NewGuid(), name, state, DateTimeOffset.Parse("2026-11-26T10:00:00Z"), null, message);

    [Fact]
    public void NoClient_HasNoSummary_AndNoText()
    {
        ClientHealthSummary.Of([]).ShouldBeNull();
        ClientHealthSummary.Text([]).ShouldBeNull();
    }

    [Theory]
    [InlineData(ClientHealthState.Healthy, ClientHealthState.Healthy, ClientHealthState.Healthy, "Healthy")]
    [InlineData(ClientHealthState.Healthy, ClientHealthState.Unknown, ClientHealthState.Unknown, "Unknown")]
    [InlineData(ClientHealthState.Healthy, ClientHealthState.Degraded, ClientHealthState.Degraded, "1 of 2 not healthy")]
    [InlineData(ClientHealthState.Degraded, ClientHealthState.Unknown, ClientHealthState.Degraded, "1 of 2 not healthy")]
    [InlineData(ClientHealthState.Healthy, ClientHealthState.Unhealthy, ClientHealthState.Unhealthy, "1 of 2 not healthy")]
    [InlineData(ClientHealthState.Unreachable, ClientHealthState.Unhealthy, ClientHealthState.Unhealthy, "2 of 2 not healthy")]
    [InlineData(ClientHealthState.Degraded, ClientHealthState.Unreachable, ClientHealthState.Unhealthy, "2 of 2 not healthy")]
    public void TheWorstStateWins(ClientHealthState first, ClientHealthState second, ClientHealthState summary, string text)
    {
        IReadOnlyList<ClientHealthInfo> clients = [Client("a", first), Client("b", second)];
        ClientHealthSummary.Of(clients).ShouldBe(summary);
        ClientHealthSummary.Text(clients).ShouldBe(text);
    }

    [Fact]
    public void TheDetails_HaveOneLinePerClient_WithTheMessage()
    {
        var text = ClientHealthSummary.Details([Client("shop", ClientHealthState.Healthy), Client("api", ClientHealthState.Unhealthy, "The client answered 503.")]);
        text.Split('\n').Length.ShouldBe(2);
        text.ShouldContain("shop: Healthy");
        text.ShouldContain("api: Unhealthy");
        text.ShouldContain("The client answered 503.");
    }
}
