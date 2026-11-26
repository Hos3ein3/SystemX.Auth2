namespace SystemX.Auth2.Contracts.Constants;

/// <summary>
/// The routes of the admin API (<see cref="Auth2Routes.Admin"/>), as <b>templates</b>. The Server maps them and the Client builds its calls from them, so they never disagree.
/// A template has the names of the route values in braces (<c>{id:guid}</c>, <c>{key}</c>); <see cref="Fill"/> puts real values in.
/// <list type="bullet">
/// <item><description><b>Server level</b> (relative to <see cref="Auth2Routes.Admin"/>): <see cref="Projects"/>, <see cref="Project"/>, <see cref="Keys"/>, ...</description></item>
/// <item><description><b>Project level</b> (relative to <see cref="ProjectBase"/>, the project is in the route): users, roles, permissions, groups, scopes, audit, external identity.</description></item>
/// <item><description><b>Application level</b> (relative to <see cref="ApplicationsBase"/>).</description></item>
/// </list>
/// </summary>
public static class Auth2AdminRoutes
{
    // ---------------------------------------------------------------- server level (relative to Auth2Routes.Admin)

    /// <summary>Projects (list, create).</summary>
    public const string Projects = "/projects";
    /// <summary>The project of a route: the base of every project-level route (<c>/projects/{projectId}</c>).</summary>
    public const string ProjectBase = "/projects/{projectId:guid}";
    /// <summary>One project (read, change, delete).</summary>
    public const string Project = ProjectBase;
    /// <summary>The settings of a project.</summary>
    public const string ProjectSettings = ProjectBase + "/settings";
    /// <summary>The messaging (email and SMS) settings of a project.</summary>
    public const string ProjectMessaging = ProjectBase + "/messaging";
    /// <summary>The applications of a project: the base of every application-level route.</summary>
    public const string ApplicationsBase = ProjectBase + "/applications";
    /// <summary>The signing keys (GlobalAdmin).</summary>
    public const string Keys = "/keys";
    /// <summary>Rotates the signing keys (GlobalAdmin).</summary>
    public const string KeysRotate = "/keys/rotate";
    /// <summary>The passkey authenticators (AAGUIDs) that are blocked (GlobalAdmin).</summary>
    public const string BlockedAaguids = "/passkeys/blocked-aaguids";

    // ---------------------------------------------------------------- application level (relative to ApplicationsBase)

    /// <summary>Applications of the project (list, create).</summary>
    public const string Applications = "/";
    /// <summary>One application (read, change, delete).</summary>
    public const string Application = "/{applicationId:guid}";
    /// <summary>The copy-paste setup of an application.</summary>
    public const string ApplicationSetup = Application + "/setup";
    /// <summary>Rotates the secret of an application.</summary>
    public const string ApplicationRotateSecret = Application + "/secret/rotate";
    /// <summary>The settings of an application.</summary>
    public const string ApplicationSettings = Application + "/settings";
    /// <summary>The messaging settings of an application.</summary>
    public const string ApplicationMessaging = Application + "/messaging";
    /// <summary>The redirect addresses of an application.</summary>
    public const string ApplicationRedirectUris = Application + "/redirect-uris";

    // ---------------------------------------------------------------- project level (relative to ProjectBase): users

    /// <summary>Users of the project (list, create).</summary>
    public const string Users = "/users";
    /// <summary>One user.</summary>
    public const string User = "/users/{id:guid}";
    /// <summary>Roles, permissions and groups of a user now.</summary>
    public const string UserAccess = User + "/access";
    /// <summary>Locks a user.</summary>
    public const string UserLock = User + "/lock";
    /// <summary>Unlocks a user.</summary>
    public const string UserUnlock = User + "/unlock";
    /// <summary>Turns the two-factor of a user off.</summary>
    public const string UserReset2Fa = User + "/reset-2fa";
    /// <summary>Open sessions of a user.</summary>
    public const string UserSessions = User + "/sessions";
    /// <summary>One session of a user.</summary>
    public const string UserSession = User + "/sessions/{sessionId}";
    /// <summary>Makes a user ProjectAdmin.</summary>
    public const string UserProjectAdmin = User + "/project-admin";
    /// <summary>A role of a user (give, take).</summary>
    public const string UserRole = User + "/roles/{roleId:guid}";
    /// <summary>A direct permission of a user (give, take).</summary>
    public const string UserPermission = User + "/permissions/{key}";
    /// <summary>A group of a user (join, leave).</summary>
    public const string UserGroup = User + "/groups/{groupId:guid}";
    /// <summary>The external sign-ins linked to a user.</summary>
    public const string UserExternalLogins = User + "/external-logins";
    /// <summary>One external sign-in of a user.</summary>
    public const string UserExternalLogin = User + "/external-logins/{provider}";
    /// <summary>The passkeys of a user.</summary>
    public const string UserPasskeys = User + "/passkeys";
    /// <summary>One passkey of a user.</summary>
    public const string UserPasskey = User + "/passkeys/{passkeyId}";

    // ---------------------------------------------------------------- project level: roles, permissions, groups, scopes

    /// <summary>Roles (list, create).</summary>
    public const string Roles = "/roles";
    /// <summary>One role (read, change, delete).</summary>
    public const string Role = "/roles/{id:guid}";
    /// <summary>The permissions of a role.</summary>
    public const string RolePermissions = Role + "/permissions";
    /// <summary>One permission of a role (add, remove).</summary>
    public const string RolePermission = Role + "/permissions/{key}";
    /// <summary>Permissions (list, create).</summary>
    public const string Permissions = "/permissions";
    /// <summary>One permission (read, change, delete).</summary>
    public const string Permission = "/permissions/{id:guid}";
    /// <summary>Marks a permission obsolete, or not.</summary>
    public const string PermissionObsolete = Permission + "/obsolete";
    /// <summary>Groups (list, create).</summary>
    public const string Groups = "/groups";
    /// <summary>One group (read, change, delete).</summary>
    public const string Group = "/groups/{id:guid}";
    /// <summary>The members of a group.</summary>
    public const string GroupMembers = Group + "/members";
    /// <summary>One member of a group (add, remove).</summary>
    public const string GroupMember = Group + "/members/{userId:guid}";
    /// <summary>One role of a group (add, remove).</summary>
    public const string GroupRole = Group + "/roles/{roleId:guid}";
    /// <summary>One permission of a group (add, remove).</summary>
    public const string GroupPermission = Group + "/permissions/{key}";
    /// <summary>Scopes (list, create). Scopes belong to the whole server; only a GlobalAdmin changes them.</summary>
    public const string Scopes = "/scopes";
    /// <summary>One scope (read, change, delete).</summary>
    public const string Scope = "/scopes/{id:guid}";

    // ---------------------------------------------------------------- project level: audit and external identity

    /// <summary>The audit events of the project.</summary>
    public const string AuditEvents = "/audit-events";
    /// <summary>The kinds of external providers that can be added.</summary>
    public const string ExternalProviderTypes = "/external-provider-types";
    /// <summary>External providers (list, create).</summary>
    public const string ExternalProviders = "/external-providers";
    /// <summary>One external provider (change, delete).</summary>
    public const string ExternalProvider = "/external-providers/{name}";
    /// <summary>Replaces the secret of an external provider.</summary>
    public const string ExternalProviderRotateSecret = ExternalProvider + "/rotate-secret";
    /// <summary>Tests an external provider.</summary>
    public const string ExternalProviderTest = ExternalProvider + "/test";
    /// <summary>Tests a user against an LDAP provider.</summary>
    public const string ExternalProviderLdapTestUser = ExternalProvider + "/ldap/test-user";
    /// <summary>Reads the SAML metadata of a provider again.</summary>
    public const string ExternalProviderSamlRefreshMetadata = ExternalProvider + "/saml/refresh-metadata";
    /// <summary>Makes a new SAML service provider certificate.</summary>
    public const string ExternalProviderSamlGenerateSpCertificate = ExternalProvider + "/saml/generate-sp-certificate";
    /// <summary>Mappings of external groups and claims to roles, groups and permissions (list, create).</summary>
    public const string ExternalGroupMappings = "/external-group-mappings";
    /// <summary>One mapping (change, delete).</summary>
    public const string ExternalGroupMapping = "/external-group-mappings/{mappingId:guid}";
    /// <summary>Email domain to provider routes.</summary>
    public const string EmailDomainRoutes = "/email-domain-routes";

    // ---------------------------------------------------------------- helpers for callers

    /// <summary>
    /// Puts values into a template: <c>Fill(Role, ("id", roleId))</c> gives <c>/roles/6f1c...</c>. A <see cref="Guid"/> is written in the <c>D</c> format, other values are escaped for a path.
    /// A value that is not in the template is ignored.
    /// </summary>
    public static string Fill(string template, params (string Name, object Value)[] values)
    {
        var path = template;
        foreach (var (name, value) in values)
        {
            var text = value is Guid g ? g.ToString("D") : Uri.EscapeDataString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
            path = path.Replace("{" + name + ":guid}", text, StringComparison.Ordinal).Replace("{" + name + "}", text, StringComparison.Ordinal);
        }
        return path;
    }

    /// <summary>The full path of a project-level route: <c>/api/auth2/admin/projects/{projectId}</c> + the route, with the values filled in.</summary>
    public static string ForProject(Guid projectId, string route, params (string Name, object Value)[] values) =>
        Auth2Routes.Admin + Fill(ProjectBase + route, [("projectId", projectId), .. values]);
}
