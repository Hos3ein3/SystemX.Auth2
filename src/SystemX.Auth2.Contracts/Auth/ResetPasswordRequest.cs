namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Finish a password reset with the token from the email or SMS.</summary>
public sealed record ResetPasswordRequest(string Login, string Token, string NewPassword);
