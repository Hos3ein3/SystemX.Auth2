namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Result of register. Registration is idempotent.</summary>
public sealed record RegisterResponse(Guid UserId, bool UserWasCreated, bool JoinedApplication, bool ConfirmationRequired);
