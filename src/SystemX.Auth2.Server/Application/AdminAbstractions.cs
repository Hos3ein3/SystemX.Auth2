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
/// <param name="IsGlobalAdmin">True for a GlobalAdmin (token from the <c>_global</c> project or the single project).</param>
/// <param name="Home">The application of the token.</param>
/// <param name="Target">The application the call works on (a global admin may choose it).</param>
public sealed record AdminContext(Guid UserId, bool IsGlobalAdmin, Project Home, Project Target);

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

    /// <summary>Runs bulk actions on multiple users in the application.</summary>
    Task<AdminBulkUserResponse> BulkActionAsync(AdminBulkUserRequest request, CancellationToken ct = default);
}

/// <summary>Admin work on projects (global admin), and on the own project (project admin: read and settings).</summary>
public interface IAdminProjectService
{
    /// <summary>Pages through projects. Only a global admin calls this.</summary>
    Task<PagedResult<ProjectDto>> ListAsync(PageQuery query, CancellationToken ct = default);

    /// <summary>One project, or null.</summary>
    Task<ProjectDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a project (multi-project mode only).</summary>
    Task<(OperationResult Result, CreateProjectResponse? Response)> CreateAsync(CreateProjectRequest request, CancellationToken ct = default);

    /// <summary>Updates name, description and the enabled flag. The project key never changes.</summary>
    Task<(OperationResult Result, ProjectDto? Project)> UpdateAsync(Guid id, UpdateProjectRequest request, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Enables or disables a project. Disabling revokes its tokens and sessions and is audited with the reason.</summary>
    Task<OperationResult> SetEnabledAsync(Guid id, bool isEnabled, string? reason, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Deletes a project with its data and its applications. Not <c>_global</c>.</summary>
    Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>The settings and their version.</summary>
    Task<(ProjectSettings Settings, int Version)?> GetSettingsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Replaces the settings. A project admin may change only <c>ProjectAdminEditable</c> settings.
    /// <paramref name="ifMatchVersion"/> (optional) must equal the current version.
    /// </summary>
    Task<(OperationResult Result, int Version)> UpdateSettingsAsync(Guid id, ProjectSettings settings, bool isGlobalAdmin, int? ifMatchVersion, CancellationToken ct = default);
}

/// <summary>Admin work on the applications of a project. Every method checks that the application belongs to the project of the route.</summary>
public interface IAdminApplicationService
{
    /// <summary>The applications of a project.</summary>
    Task<IReadOnlyList<ApplicationDto>> ListAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>One application of the project, or null.</summary>
    Task<ApplicationDto?> GetAsync(Guid projectId, Guid applicationId, CancellationToken ct = default);

    /// <summary>Creates an application. The secret is in the answer and is shown once.</summary>
    Task<(OperationResult Result, CreateApplicationResponse? Response)> CreateAsync(Guid projectId, CreateApplicationRequest request, string serverUrl, CancellationToken ct = default);

    /// <summary>Changes name, redirect URIs, allowed scopes and the enabled flag.</summary>
    Task<(OperationResult Result, ApplicationDto? Application)> UpdateAsync(Guid projectId, Guid applicationId, UpdateApplicationRequest request, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Deletes an application with its tokens and authorizations.</summary>
    Task<OperationResult> DeleteAsync(Guid projectId, Guid applicationId, CancellationToken ct = default);

    /// <summary>Rotates the client secret and returns the setup with the new secret (shown once). A project admin needs <c>Clients.AllowProjectAdminRotateSecret</c>.</summary>
    Task<(OperationResult Result, ClientSetupDto? Setup)> RotateSecretAsync(Guid projectId, Guid applicationId, bool isGlobalAdmin, string serverUrl, CancellationToken ct = default);

    /// <summary>The copy-paste setup of an application (without the secret).</summary>
    Task<(OperationResult Result, ClientSetupDto? Setup)> GetSetupAsync(Guid projectId, Guid applicationId, string serverUrl, CancellationToken ct = default);

    /// <summary>The settings overrides of an application and their version.</summary>
    Task<(OperationResult Result, ClientApplicationSettings? Settings, int Version)> GetSettingsAsync(Guid projectId, Guid applicationId, CancellationToken ct = default);

    /// <summary>Replaces the settings overrides.</summary>
    Task<(OperationResult Result, int Version)> UpdateSettingsAsync(Guid projectId, Guid applicationId, ClientApplicationSettings settings, bool isGlobalAdmin, int? ifMatchVersion, CancellationToken ct = default);
}

/// <summary>Admin work on OpenIddict scopes.</summary>
public interface IAdminScopeService
{
    /// <summary>Lists scopes with pagination, search, and filtering.</summary>
    Task<PagedResult<AdminScopeDto>> ListAsync(PageQuery query, string? owner = null, CancellationToken ct = default);

    /// <summary>Gets one scope by id.</summary>
    Task<AdminScopeDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a scope.</summary>
    Task<(OperationResult Result, AdminScopeDto? Scope)> CreateAsync(CreateScopeRequest request, bool isGlobalAdmin, CancellationToken ct = default);

    /// <summary>Updates an existing scope.</summary>
    Task<(OperationResult Result, AdminScopeDto? Scope)> UpdateAsync(Guid id, UpdateScopeRequest request, bool isGlobalAdmin, CancellationToken ct = default);

    /// <summary>Deletes a scope if not in use by any application.</summary>
    Task<OperationResult> DeleteAsync(Guid id, bool isGlobalAdmin, CancellationToken ct = default);
}

/// <summary>Admin work on OpenIddict authorizations.</summary>
public interface IAdminAuthorizationService
{
    /// <summary>Lists authorizations with pagination and filters.</summary>
    Task<PagedResult<AdminAuthorizationDto>> ListAsync(PageQuery query, string? subject = null, string? status = null, CancellationToken ct = default);

    /// <summary>Revokes one authorization and its tokens.</summary>
    Task<OperationResult> RevokeAsync(string id, CancellationToken ct = default);

    /// <summary>Revokes authorizations by application or subject.</summary>
    Task<OperationResult> RevokeAllAsync(string? applicationId, string? subject, CancellationToken ct = default);
}

/// <summary>Admin work on active/revoked sessions across the application.</summary>
public interface IAdminSessionService
{
    /// <summary>Lists sessions with pagination and filters.</summary>
    Task<PagedResult<AdminSessionDto>> ListAsync(PageQuery query, string? subject = null, string? status = null, string? ip = null, CancellationToken ct = default);

    /// <summary>Revokes one session by authorization id.</summary>
    Task<OperationResult> RevokeAsync(string id, CancellationToken ct = default);

    /// <summary>Revokes all sessions for a subject or the whole application.</summary>
    Task<OperationResult> RevokeAllAsync(string? subject, CancellationToken ct = default);
}

/// <summary>Admin work on branding assets and email templates.</summary>
public interface IAdminBrandingService
{
    /// <summary>Lists branding assets of an application.</summary>
    Task<IReadOnlyList<BrandingAssetDto>> ListAssetsAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>Gets raw binary branding asset.</summary>
    Task<BrandingAsset?> GetAssetAsync(Guid applicationId, string kind, CancellationToken ct = default);

    /// <summary>Uploads or replaces a branding asset.</summary>
    Task<(OperationResult Result, BrandingAssetDto? Asset)> UploadAssetAsync(Guid applicationId, string kind, string contentType, byte[] data, CancellationToken ct = default);

    /// <summary>Deletes a branding asset.</summary>
    Task<OperationResult> DeleteAssetAsync(Guid applicationId, string kind, CancellationToken ct = default);

    /// <summary>Lists email templates of an application.</summary>
    Task<IReadOnlyList<EmailTemplateDto>> ListTemplatesAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>Gets one email template.</summary>
    Task<EmailTemplateDto?> GetTemplateAsync(Guid applicationId, string name, string culture, CancellationToken ct = default);

    /// <summary>Saves or updates an email template.</summary>
    Task<(OperationResult Result, EmailTemplateDto? Template)> SaveTemplateAsync(Guid applicationId, string name, string culture, string subject, string body, CancellationToken ct = default);

    /// <summary>Resets an email template to default.</summary>
    Task<OperationResult> ResetTemplateAsync(Guid applicationId, string name, string culture, CancellationToken ct = default);
}

/// <summary>Admin work on maintenance jobs and job run history.</summary>
public interface IAdminJobService
{
    /// <summary>Gets status summary of all maintenance jobs.</summary>
    Task<IReadOnlyList<JobStatusDto>> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Lists job run history records.</summary>
    Task<PagedResult<JobRunDto>> ListRunsAsync(PageQuery query, string? jobName = null, bool? success = null, CancellationToken ct = default);

    /// <summary>Runs a maintenance job immediately.</summary>
    Task<(OperationResult Result, JobRunDto? Run)> RunNowAsync(string jobName, CancellationToken ct = default);
}

/// <summary>Reads audit events.</summary>
public interface IAuditReader
{
    /// <summary>
    /// Pages through events, newest first. With <paramref name="allProjects"/> every application is included
    /// (only a global admin may ask); otherwise the current application only.
    /// </summary>
    Task<PagedResult<AuditEventDto>> QueryAsync(PageQuery query, string? action, Guid? actorUserId, string? targetType,
        DateTimeOffset? from, DateTimeOffset? to, bool allProjects, CancellationToken ct = default);
}
