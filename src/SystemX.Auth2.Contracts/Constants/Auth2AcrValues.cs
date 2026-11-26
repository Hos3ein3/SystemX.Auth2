namespace SystemX.Auth2.Contracts.Constants;

/// <summary>
/// Values of the <c>acr_values</c> parameter that an application can put on an authorize request (<c>/connect/authorize</c>) to ask the Server for more than a sign-in.
/// </summary>
public static class Auth2AcrValues
{
    /// <summary>
    /// "Prove now that you hold the authenticator app." The Server shows its <c>/Account/VerifyTotp</c> page when the person has not given a code in this session within the last minutes
    /// (<c>TwoFactor.StepUpMinutes</c>, the one setting that every part of Auth2 uses), and then continues the sign-in. The tokens that come back carry the claims <see cref="Auth2Claims.StepUpAt"/> and <see cref="Auth2Claims.StepUpUntil"/>.
    /// A person who has not turned on two-factor is sent to the page that explains how to turn it on. The Client package asks for this value by itself when a hosted-UI request
    /// reaches <c>WithTotp()</c> / <c>[Auth2Totp]</c> without a fresh code.
    /// </summary>
    public const string Totp = "urn:systemx:auth2:acr:totp";

}
