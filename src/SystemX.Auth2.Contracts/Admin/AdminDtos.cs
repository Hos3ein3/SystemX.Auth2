namespace SystemX.Auth2.Contracts.Admin;

/// <summary>Answer of "create project".</summary>
public sealed record CreateProjectResponse(Contracts.Applications.ProjectDto Project);

/// <summary>Answer of "create application". <c>ClientSecret</c> is shown only this once. <c>Setup</c> is the copy-paste client configuration.</summary>
public sealed record CreateApplicationResponse(Contracts.Applications.ApplicationDto Application, string ClientSecret, ClientSetupDto Setup);

/// <summary>
/// What a client needs in its configuration. <c>ClientSecret</c> is only set right after creation or rotation (shown once).
/// <c>ProjectKey</c> is null on a single-project server. <c>Snippet</c> is C# code to paste into the client's startup.
/// </summary>
public sealed record ClientSetupDto(string ServerUrl, string? ProjectKey, string ApplicationName, string ClientId, string? ClientSecret, string Snippet);

/// <summary>Answer of "rotate secret". The new secret is shown only this once.</summary>
public sealed record RotateSecretResponse(string ClientSecret, ClientSetupDto? Setup = null);

/// <summary>Redirect URIs of an application (the OpenIddict fields). Exact match, no wildcards.</summary>
public sealed record RedirectUrisDto(IReadOnlyList<string> RedirectUris, IReadOnlyList<string> PostLogoutRedirectUris);

/// <summary>Create a user as an admin. If the person already exists, only the membership is added and no password is set.</summary>
public sealed record AdminCreateUserRequest(string? UserName, string? Email, string? PhoneNumber, string? Password,
    string? FirstName = null, string? LastName = null);

/// <summary>Answer of "create user".</summary>
public sealed record AdminCreateUserResponse(Guid UserId, bool UserWasCreated, bool JoinedProject);

/// <summary>Change a user's profile. Null means "leave as it is".</summary>
public sealed record AdminUpdateUserRequest(string? FirstName, string? LastName, string? DisplayName, string? PreferredCulture, bool? IsActive);

/// <summary>Lock a user. Null <c>UntilUtc</c> means until an admin unlocks.</summary>
public sealed record LockUserRequest(DateTimeOffset? UntilUtc = null);

/// <summary>A role with its id, for the admin screens.</summary>
public sealed record AdminRoleDto(Guid Id, string Name, string? Description, bool IsGlobal, bool IsSystem, IReadOnlyList<string> Permissions);

/// <summary>A permission with its id.</summary>
public sealed record AdminPermissionDto(Guid Id, string Key, string? DisplayName, string? Description, string? Group, bool IsObsolete, string Source);

/// <summary>A group with its id.</summary>
public sealed record AdminGroupDto(Guid Id, string Name, string? DisplayName, string? Description, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, int MemberCount);

/// <summary>Create a role.</summary>
public sealed record CreateRoleRequest(string Name, string? Description = null);

/// <summary>Create a group.</summary>
public sealed record CreateGroupRequest(string Name, string? DisplayName = null, string? Description = null);

/// <summary>Mark a permission obsolete (or not).</summary>
public sealed record SetObsoleteRequest(bool IsObsolete);

/// <summary>One audit event. No secrets are ever stored in it.</summary>
public sealed record AuditEventDto(Guid Id, DateTimeOffset OccurredAtUtc, Guid? ProjectId, Guid? ActorUserId, string? ActorClientId,
    string Action, string? TargetType, string? TargetId, string? IpAddress, string? UserAgent, string? DataJson, Guid? ApplicationId = null);

/// <summary>One open session (an OpenIddict authorization) of a user.</summary>
public sealed record SessionDto(string Id, DateTimeOffset? CreatedAtUtc, IReadOnlyList<string> Scopes);

// --- Scopes ---

/// <summary>A scope definition for admin management.</summary>
public sealed record AdminScopeDto(Guid Id, string Name, string? DisplayName, string? Description, IReadOnlyList<string> Resources, bool IsGlobal, Guid? ProjectId);

/// <summary>Request to create a scope.</summary>
public sealed record CreateScopeRequest(string Name, string? DisplayName = null, string? Description = null, IReadOnlyList<string>? Resources = null);

/// <summary>Request to update a scope.</summary>
public sealed record UpdateScopeRequest(string? DisplayName = null, string? Description = null, IReadOnlyList<string>? Resources = null);

// --- Authorizations ---

/// <summary>An OpenIddict authorization for admin management.</summary>
public sealed record AdminAuthorizationDto(string Id, string? Subject, string? ProjectId, string? Status, string? Type, IReadOnlyList<string> Scopes, DateTimeOffset? CreatedAtUtc);

// --- Sessions ---

/// <summary>An active or revoked session for admin management.</summary>
public sealed record AdminSessionDto(string Id, string? Subject, string? ProjectId, string? Status, IReadOnlyList<string> Scopes, DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastSeenAtUtc = null, string? IpAddress = null, string? UserAgent = null);

// --- Entity Updates (Roles, Permissions, Groups) ---

/// <summary>Request to update a role.</summary>
public sealed record UpdateRoleRequest(string? Name = null, string? Description = null);

/// <summary>Request to update a permission.</summary>
public sealed record UpdatePermissionRequest(string? DisplayName = null, string? Description = null, string? Group = null);

/// <summary>Request to update a group.</summary>
public sealed record UpdateGroupRequest(string? DisplayName = null, string? Description = null);

// --- Bulk Users ---

/// <summary>Request to perform a bulk operation on users.</summary>
public sealed record AdminBulkUserRequest(IReadOnlyList<Guid> UserIds, string Action, string? Value = null);

/// <summary>Result of a bulk operation on users.</summary>
public sealed record AdminBulkUserResponse(int Succeeded, int Failed, IReadOnlyList<string> Errors);

// --- Maintenance Jobs ---

/// <summary>History record of a maintenance job run.</summary>
public sealed record JobRunDto(Guid Id, string JobName, string? InstanceId, DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc, bool Success, string? Error, int RowsAffected);

/// <summary>Status summary of a maintenance job.</summary>
public sealed record JobStatusDto(string Name, DateTimeOffset? LastRunUtc, DateTimeOffset? LastSuccessUtc, string? LastError, bool IsHealthy);

// --- Branding Assets ---

/// <summary>A branding asset stored in the database.</summary>
public sealed record BrandingAssetDto(Guid Id, Guid ProjectId, string Kind, string ContentType, long Size, string Sha256, DateTimeOffset UpdatedAtUtc);

/// <summary>Request to upload a branding asset.</summary>
public sealed record UploadBrandingAssetRequest(string Kind, string ContentType, string Base64Data);

// --- Email Templates ---

/// <summary>An email template.</summary>
public sealed record EmailTemplateDto(Guid Id, Guid ProjectId, string Name, string Culture, string Subject, string Body, bool IsCustom, DateTimeOffset UpdatedAtUtc);

/// <summary>Request to save an email template.</summary>
public sealed record SaveEmailTemplateRequest(string Subject, string Body);

