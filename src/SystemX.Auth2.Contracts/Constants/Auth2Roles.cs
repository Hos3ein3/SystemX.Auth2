namespace SystemX.Auth2.Contracts.Constants;

/// <summary>Built-in role and project names.</summary>
public static class Auth2Roles
{
    /// <summary>Global admin role (global, no application).</summary>
    public const string GlobalAdmin = "GlobalAdmin";
    /// <summary>Developer role with access to developer dashboard and system administration.</summary>
    public const string Developer = "Developer";
    /// <summary>Project admin role (one per application).</summary>
    public const string ProjectAdmin = "ProjectAdmin";
    /// <summary>Project key of the built-in global project.</summary>
    public const string GlobalProjectKey = "_global";
}
