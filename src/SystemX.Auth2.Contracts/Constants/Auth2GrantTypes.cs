namespace SystemX.Auth2.Contracts.Constants;

/// <summary>Custom OAuth grant types of SystemX.Auth2.</summary>
public static class Auth2GrantTypes
{
    /// <summary>Second step of a login. Inputs: <c>two_factor_token</c> and <c>code</c> or <c>recovery_code</c>.</summary>
    public const string TwoFactor = "urn:systemx:auth2:grant:two_factor";

    /// <summary>Login with a one-time code. The code is requested with <c>POST /api/auth2/otp/request</c>.</summary>
    public const string Otp = "urn:systemx:auth2:grant:otp";
}
