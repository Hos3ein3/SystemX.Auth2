namespace SystemX.Auth2.Contracts.Users;

/// <summary>A user as shown in the admin API.</summary>
public sealed record UserDto(Guid Id, string? UserName, string? Email, bool EmailConfirmed, string? PhoneNumber,
    bool PhoneNumberConfirmed, string? FirstName, string? LastName, string? DisplayName, bool IsActive,
    bool IsLockedOut, bool TwoFactorEnabled, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastLoginUtc);
