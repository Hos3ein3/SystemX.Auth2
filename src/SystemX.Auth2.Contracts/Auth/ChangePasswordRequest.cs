namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Change the own password for the token's application.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
