using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Application;

/// <summary>Use case for self-service user profile operations.</summary>
public interface IProfileService
{
    /// <summary>Updates name, display name, and preferred culture.</summary>
    Task<OperationResult> UpdateProfileAsync(AppUser user, string? firstName, string? lastName, string? displayName, string? culture, CancellationToken ct = default);

    /// <summary>Starts an email change workflow.</summary>
    Task<OperationResult> RequestEmailChangeAsync(AppUser user, string newEmail, string? returnUrl = null, CancellationToken ct = default);

    /// <summary>Completes an email change workflow.</summary>
    Task<OperationResult> ConfirmEmailChangeAsync(AppUser user, string newEmail, string token, CancellationToken ct = default);

    /// <summary>Deletes the user's membership from the current project.</summary>
    Task<OperationResult> DeleteMembershipAsync(AppUser user, Guid projectId, CancellationToken ct = default);
}
