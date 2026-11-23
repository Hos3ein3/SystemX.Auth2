namespace SystemX.Auth2.Contracts.Constants;

/// <summary>Claim names used in tokens.</summary>
public static class Auth2Claims
{
    /// <summary>Subject (user id).</summary>
    public const string Subject = "sub";
    /// <summary>User name.</summary>
    public const string PreferredUserName = "preferred_username";
    /// <summary>Email.</summary>
    public const string Email = "email";
    /// <summary>Phone number.</summary>
    public const string PhoneNumber = "phone_number";
    /// <summary>First name.</summary>
    public const string GivenName = "given_name";
    /// <summary>Last name.</summary>
    public const string FamilyName = "family_name";
    /// <summary>Full name.</summary>
    public const string Name = "name";
    /// <summary>Role of the application.</summary>
    public const string Role = "role";
    /// <summary>Scope.</summary>
    public const string Scope = "scope";
    /// <summary>Client id.</summary>
    public const string ClientId = "client_id";
    /// <summary>Access version.</summary>
    public const string AccessVersion = "pv";
    /// <summary>Application id.</summary>
    public const string ApplicationId = "app";
    /// <summary>Project key. Omitted in single-application mode.</summary>
    public const string ProjectKey = "projectkey";
    /// <summary>Authentication methods reference.</summary>
    public const string Amr = "amr";
    /// <summary>Session id.</summary>
    public const string SessionId = "sid";
}
