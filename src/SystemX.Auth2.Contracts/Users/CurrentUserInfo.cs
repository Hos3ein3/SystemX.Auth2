namespace SystemX.Auth2.Contracts.Users;

/// <summary>The signed-in user and their access, for the token's application.</summary>
public sealed record CurrentUserInfo(Guid UserId, string? UserName, string? Email, string? PhoneNumber,
    string? FirstName, string? LastName, string? FullName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Groups,
    IReadOnlyList<string> Scopes, int AccessVersion);
