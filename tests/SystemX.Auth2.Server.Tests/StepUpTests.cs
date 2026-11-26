using System.Security.Claims;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Tests;

/// <summary>"Sudo mode": the time of the last code of the authenticator app is a claim of the session; the restricted pages ask again when it is too old.</summary>
public class StepUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static ClaimsPrincipal Session(string? stepUpAt = null) =>
        new(new ClaimsIdentity(stepUpAt is null ? [] : [new Claim(StepUp.Claim, stepUpAt)], "test"));

    private static string At(TimeSpan ago) => Now.Add(-ago).ToUnixTimeSeconds().ToString();

    [Theory]
    [InlineData(0, true)]      // just now
    [InlineData(14, true)]     // 14 of 15 minutes
    [InlineData(15, true)]     // exactly the window
    [InlineData(16, false)]    // too old
    [InlineData(600, false)]
    public void ACodeIsFresh_OnlyInsideTheWindow(int minutesAgo, bool fresh) =>
        StepUp.IsFresh(Session(At(TimeSpan.FromMinutes(minutesAgo))), 15, Now).ShouldBe(fresh);

    [Fact]
    public void NoCode_ABrokenClaim_OrACodeFromTheFuture_NeverCounts()
    {
        StepUp.IsFresh(Session(), 15, Now).ShouldBeFalse();                                   // never gave a code in this session
        StepUp.IsFresh(Session("not a number"), 15, Now).ShouldBeFalse();
        StepUp.IsFresh(Session("-5"), 15, Now).ShouldBeFalse();
        StepUp.IsFresh(Session(Now.AddHours(5).ToUnixTimeSeconds().ToString()), 15, Now).ShouldBeFalse();   // a forged or wrong clock
        StepUp.LastAt(Session("x")).ShouldBeNull();
    }

    [Fact]
    public void TheWindow_IsAGlobalSetting_Default15Minutes_AndChecked()
    {
        new ProjectSettings().TwoFactor.StepUpMinutes.ShouldBe(15);
        var attribute = (SettingEditabilityAttribute)Attribute.GetCustomAttribute(typeof(TwoFactorSettings).GetProperty("StepUpMinutes")!, typeof(SettingEditabilityAttribute))!;
        attribute.Editor.ShouldBe(SettingEditor.GlobalOnly);

        ProjectSettingsValidator.Validate(new ProjectSettings { TwoFactor = { StepUpMinutes = 0 } }).ShouldBeEmpty();      // 0 = never ask again
        ProjectSettingsValidator.Validate(new ProjectSettings { TwoFactor = { StepUpMinutes = 720 } }).ShouldBeEmpty();
        ProjectSettingsValidator.Validate(new ProjectSettings { TwoFactor = { StepUpMinutes = -1 } }).ShouldContain(e => e.Path == "TwoFactor.StepUpMinutes");
        ProjectSettingsValidator.Validate(new ProjectSettings { TwoFactor = { StepUpMinutes = 721 } }).ShouldContain(e => e.Path == "TwoFactor.StepUpMinutes");
    }
}
