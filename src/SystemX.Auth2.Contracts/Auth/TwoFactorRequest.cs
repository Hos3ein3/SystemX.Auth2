namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Second step of login. Send <c>Code</c> or <c>RecoveryCode</c>.</summary>
public sealed record TwoFactorRequest(string TwoFactorToken, string? Code = null, string? RecoveryCode = null);
