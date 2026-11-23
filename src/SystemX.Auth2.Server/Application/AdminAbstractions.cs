using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Users;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>Paging, search and sorting of a list. Page size is limited to <see cref="PagedResult{T}.MaxPageSize"/>.</summary>
public sealed record PageQuery(int Page = 1, int PageSize = 50, string? Search = null, string? Sort = null, bool Descending = false)
{
    /// <summary>Page, at least 1.</summary>
    public int SafePage => Math.Max(1, Page);
    /// <summary>Page size between 1 and 200.</summary>
    public int SafePageSize => Math.Clamp(PageSize, 1, PagedResult<object>.MaxPageSize);
    /// <summary>Rows to skip.</summary>
    public int Skip => (SafePage - 1) * SafePageSize;
    /// <summary>Search text, trimmed, or null.</summary>
    public string? SafeSearch => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}

/// <summary>A user with the tag used for <c>If-Match</c>.</summary>
public sealed record AdminUserView(UserDto User, string ETag);

/// <summary>Who is calling the admin API and which application the call works on.</summary>
/// <param name="UserId">The admin.</param>
/// <param name="IsGlobalAdmin">True for a GlobalAdmin (token from the <c>_global</c> project or the single application).</param>
/// <param name="Home">The application of the token.</param>
/// <param name="Target">The application the call works on (a global admin may choose it).</param>
public sealed record AdminContext(Guid UserId, bool IsGlobalAdmin, Auth2Application Home, Auth2Application Target);

/// <summary>Admin work on the users of the <b>current application</b>.</summary>
public interface IAdminUserService
{
    /// <summary>Pages through the users of the application. Sort: <c>username</c>, <c>email</c>, <c>created</c> (default), <c>lastlogin</c>.</summary>
    Task<PagedResult<UserDto>> ListAsync(PageQuery query, bool? lockedOut = null, CancellationToken ct = default);

    /// <summary>One user of the application, or null.</summary>
    Task<AdminUserView?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Creates a user and the membership. When the person exists already, only the membership is added: an admin can never set
    /// a password for an existing person (the person uses the password reset or join flow).
    /// </summary>
    Task<(OperationResult Result, AdminCreateUserResponse? Response)> CreateAsync(AdminCreateUserRequest request, CancellationToken ct = default);

    /// <summary>Updates the profile. <paramref name="ifMatch"/> is the tag from <see cref="AdminUserView.ETag"/> (optional).</summary>
    Task<(OperationResult Result, AdminUserView? User)> UpdateAsync(Guid id, AdminUpdateUserRequest request, string? ifMatch, CancellationToken ct = default);

    /// <summary>Removes the user from the application (multi mode) or deletes the user (single mode). Sessions end.</summary>
    Task<OperationResult> RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Locks the user in this application and ends the sessions.</summary>
    Task<OperationResult> LockAsync(Guid id, DateTimeOffset? untilUtc, CancellationToken ct = default);

    /// <summary>Unlocks and resets the failed count.</summary>
    Task<OperationResult> UnlockAsync(Guid id, CancellationToken ct = default);

    /// <summary>Turns two-factor off, drops the authenticator key and recovery codes, ends the sessions.</summary>
    Task<OperationResult> ResetTwoFactorAsync(Guid id, CancellationToken ct = default);

    /// <summary>Open sessions of a user in the application.</summary>
    Task<(OperationResult Result, IReadOnlyList<SessionDto> Sessions)> ListSessionsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Ends one session, or every session when <paramref name="sessionId"/> is null.</summary>
    Task<OperationResult> RevokeSessionsAsync(Guid id, string? sessionId, CancellationToken ct = default);
}

/// <summary>Admin work on applications (global admin), and on the own application (project admin: read and settings).</summary>
public interface IAdminApplicationService
{
    /// <summary>Pages through applications. Only a global admin calls this.</summary>
    Task<PagedResult<ApplicationDto>> ListAsync(PageQuery query, CancellationToken ct = default);

    /// <summary>One application, or null.</summary>
    Task<ApplicationDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates an application and its client (multi mode only).</summary>
    Task<(OperationResult Result, CreateApplicationResponse? Response)> CreateAsync(CreateApplicationRequest request, CancellationToken ct = default);

    /// <summary>Updates name, description and the enabled flag.</summary>
    Task<(OperationResult Result, ApplicationDto? Application)> UpdateAsync(Guid id, UpdateApplicationRequest request, CancellationToken ct = default);

    /// <summary>Deletes an application with its data and its client. Not <c>_global</c>.</summary>
    Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>The settings and their version.</summary>
    Task<(ApplicationSettings Settings, int Version)?> GetSettingsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Replaces the settings. A project admin may change only <c>ProjectAdminEditable</c> settings.
    /// <paramref name="ifMatchVersion"/> (optional) must equal the current version.
    /// </summary>
    Task<(OperationResult Result, int Version)> UpdateSettingsAsync(Guid id, ApplicationSettings settings, bool isGlobalAdmin, int? ifMatchVersion, CancellationToken ct = default);

    /// <summary>Rotates the client secret. A project admin needs <c>Clients.AllowProjectAdminRotateSecret</c>.</summary>
    Task<(OperationResult Result, string? Secret)> RotateSecretAsync(Guid id, bool isGlobalAdmin, CancellationToken ct = default);
}

/// <summary>Reads audit events.</summary>
public interface IAuditReader
{
    /// <summary>
    /// Pages through events, newest first. With <paramref name="allApplications"/> every application is included
    /// (only a global admin may ask); otherwise the current application only.
    /// </summary>
    Task<PagedResult<AuditEventDto>> QueryAsync(PageQuery query, string? action, Guid? actorUserId, string? targetType,
        DateTimeOffset? from, DateTimeOffset? to, bool allApplications, CancellationToken ct = default);
}
