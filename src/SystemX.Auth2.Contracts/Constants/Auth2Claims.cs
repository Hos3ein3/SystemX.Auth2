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
    /// <summary>"true" when the email address is confirmed (needs the <c>email</c> scope, like <see cref="Email"/>).</summary>
    public const string EmailVerified = "email_verified";
    /// <summary>"true" when the phone number is confirmed (needs the <c>phone</c> scope, like <see cref="PhoneNumber"/>).</summary>
    public const string PhoneNumberVerified = "phone_number_verified";
    /// <summary>First name.</summary>
    public const string GivenName = "given_name";
    /// <summary>Last name.</summary>
    public const string FamilyName = "family_name";
    /// <summary>Full name.</summary>
    public const string Name = "name";
    /// <summary>Role of the project.</summary>
    public const string Role = "role";
    /// <summary>Scope.</summary>
    public const string Scope = "scope";
    /// <summary>Client id.</summary>
    public const string ClientId = "client_id";
    /// <summary>Access version.</summary>
    public const string AccessVersion = "pv";
    /// <summary>Application id (the client that asked for the token).</summary>
    public const string ApplicationId = "application_id";
    /// <summary>Project id (the tenant).</summary>
    public const string ProjectId = "project_id";
    /// <summary>Project key. Omitted in single-project mode.</summary>
    public const string ProjectKey = "projectkey";
    /// <summary>Authentication methods reference.</summary>
    public const string Amr = "amr";
    /// <summary>Name of the external provider the user signed in with (<c>idp</c>). Absent for local sign-in.</summary>
    public const string IdentityProvider = "idp";
    /// <summary>Session id.</summary>
    public const string SessionId = "sid";
    /// <summary>Saved culture of the user.</summary>
    public const string Locale = "locale";
    /// <summary>Set to <c>true</c> on machine (client credentials) tokens. User tokens never have it.</summary>
    public const string Machine = "machine";
}
