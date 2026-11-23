namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Start a password reset. The answer is always the same.</summary>
public sealed record ForgotPasswordRequest(string Login, string? ReturnUrl = null);
