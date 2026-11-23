namespace SystemX.Auth2.Contracts.Admin;

/// <summary>Answer of "create application". <c>ClientSecret</c> is shown only this once.</summary>
public sealed record CreateApplicationResponse(Contracts.Applications.ApplicationDto Application, string ClientSecret);

/// <summary>Answer of "rotate secret". The new secret is shown only this once.</summary>
public sealed record RotateSecretResponse(string ClientSecret);

/// <summary>Redirect URIs of an application. Exact match, no wildcards.</summary>
public sealed record RedirectUrisDto(IReadOnlyList<string> RedirectUris, IReadOnlyList<string> PostLogoutRedirectUris);

/// <summary>Create a user as an admin. If the person already exists, only the membership is added and no password is set.</summary>
public sealed record AdminCreateUserRequest(string? UserName, string? Email, string? PhoneNumber, string? Password,
    string? FirstName = null, string? LastName = null);

/// <summary>Answer of "create user".</summary>
public sealed record AdminCreateUserResponse(Guid UserId, bool UserWasCreated, bool JoinedApplication);

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
public sealed record AuditEventDto(Guid Id, DateTimeOffset OccurredAtUtc, Guid? ApplicationId, Guid? ActorUserId, string? ActorClientId,
    string Action, string? TargetType, string? TargetId, string? IpAddress, string? UserAgent, string? DataJson);

/// <summary>One open session (an OpenIddict authorization) of a user.</summary>
public sealed record SessionDto(string Id, DateTimeOffset? CreatedAtUtc, IReadOnlyList<string> Scopes);
