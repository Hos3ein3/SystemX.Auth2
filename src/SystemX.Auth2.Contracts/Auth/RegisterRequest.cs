namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Register a user. The application always comes from the client token, never from this body.</summary>
public sealed record RegisterRequest(string? UserName, string? Email, string? PhoneNumber, string Password,
    string? FirstName = null, string? LastName = null, string? ReturnUrl = null);
