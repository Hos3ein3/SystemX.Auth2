namespace SystemX.Auth2.Contracts.Access;

/// <summary>A permission as a client or an admin screen sees it.</summary>
public sealed record PermissionDto(string Key, string? DisplayName, string? Description, string? Group, bool IsObsolete);

/// <summary>A role of one application (or a global role) with its permission keys.</summary>
public sealed record RoleDto(string Name, string? Description, bool IsGlobal, bool IsSystem, IReadOnlyList<string> Permissions);

/// <summary>A group with its roles and permission keys.</summary>
public sealed record GroupDto(string Name, string? DisplayName, string? Description, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
