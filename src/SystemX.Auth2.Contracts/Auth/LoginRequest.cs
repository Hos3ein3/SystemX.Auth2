namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Login input used by the client's Self UI helpers. The token endpoint is the only issuing path.</summary>
/// <param name="Login">User name, email or phone number.</param>
/// <param name="Password">The password. Never logged.</param>
/// <param name="Scopes">Optional scopes to request.</param>
public sealed record LoginRequest(string Login, string Password, IReadOnlyList<string>? Scopes = null);
