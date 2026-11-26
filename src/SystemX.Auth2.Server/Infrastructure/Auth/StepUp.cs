using System.Globalization;
using System.Security.Claims;

namespace SystemX.Auth2.Server.Infrastructure.Auth;

/// <summary>
/// "Sudo mode" of the hosted-UI session: the time of the last code of the authenticator app is a claim of the session cookie. The restricted pages ask a person who has
/// two-factor on for a code again when it is older than <c>TwoFactor.StepUpMinutes</c>. The claim belongs to <b>this session</b> only: another browser is asked on its own.
/// </summary>
public static class StepUp
{
    /// <summary>The claim of the session cookie: Unix time in seconds of the last code.</summary>
    public const string Claim = SystemX.Auth2.Contracts.Constants.Auth2Claims.StepUpAt;

    /// <summary>When the person last gave a code in this session, or null.</summary>
    public static DateTimeOffset? LastAt(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(Claim)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    /// <summary>True when the last code is not older than <paramref name="maxAge"/>. A code from the future never counts.</summary>
    public static bool IsFresh(ClaimsPrincipal user, TimeSpan maxAge, DateTimeOffset now) =>
        LastAt(user) is { } at && at <= now && now - at <= maxAge;

    /// <summary>True when the last code is not older than <paramref name="minutes"/>. A code from the future (a wrong clock, a forged claim) never counts.</summary>
    public static bool IsFresh(ClaimsPrincipal user, int minutes, DateTimeOffset now) =>
        LastAt(user) is { } at && at <= now && now - at <= TimeSpan.FromMinutes(minutes);
}
