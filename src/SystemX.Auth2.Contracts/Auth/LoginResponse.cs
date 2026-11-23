namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Result of a login. Either tokens, or a pending second factor.</summary>
public sealed record LoginResponse(string? AccessToken, string? RefreshToken, string? IdToken, int? ExpiresInSeconds,
    bool TwoFactorRequired = false, string? TwoFactorToken = null);
