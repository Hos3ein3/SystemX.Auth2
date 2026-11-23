namespace SystemX.Auth2.Contracts.Constants;

/// <summary>Scope names defined by SystemX.Auth2.</summary>
public static class Auth2Scopes
{
    /// <summary>Register users.</summary>
    public const string Register = "auth2.register";
    /// <summary>Request OTP, reset passwords and similar login helpers.</summary>
    public const string Login = "auth2.login";
    /// <summary>Read the current user.</summary>
    public const string Me = "auth2.me";
    /// <summary>Run the client handshake.</summary>
    public const string ClientHandshake = "auth2.client.handshake";
    /// <summary>Sync permissions.</summary>
    public const string PermissionsSync = "auth2.permissions.sync";
    /// <summary>Sync roles.</summary>
    public const string RolesSync = "auth2.roles.sync";
    /// <summary>Read groups.</summary>
    public const string GroupsRead = "auth2.groups.read";
    /// <summary>Send groups (only when the application allows group sync).</summary>
    public const string GroupsSync = "auth2.groups.sync";
    /// <summary>Send and read the custom scopes of an application.</summary>
    public const string ScopesSync = "auth2.scopes.sync";
    /// <summary>Project admin.</summary>
    public const string Admin = "auth2.admin";
    /// <summary>Global admin.</summary>
    public const string AdminGlobal = "auth2.admin.global";

    /// <summary>All SystemX.Auth2 scopes.</summary>
    public static readonly IReadOnlyList<string> All =
        [Register, Login, Me, ClientHandshake, PermissionsSync, RolesSync, GroupsRead, GroupsSync, ScopesSync, Admin, AdminGlobal];
}
