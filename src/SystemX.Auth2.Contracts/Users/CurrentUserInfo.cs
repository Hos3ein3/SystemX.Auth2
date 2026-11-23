namespace SystemX.Auth2.Contracts.Users;

/// <summary>
/// The signed-in user and their access, for the token's application.
/// <c>DirectPermissions</c> are grants made to the user. <c>RolePermissions</c> come from the user's roles or the roles of the user's groups.
/// <c>Permissions</c> is everything (those two, plus permissions of the user's groups). The two lists are null when talking to an older server.
/// </summary>
public sealed record CurrentUserInfo(Guid UserId, string? UserName, string? Email, string? PhoneNumber,
    string? FirstName, string? LastName, string? FullName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Groups,
    IReadOnlyList<string> Scopes, int AccessVersion,
    IReadOnlyList<string>? DirectPermissions = null, IReadOnlyList<string>? RolePermissions = null);
