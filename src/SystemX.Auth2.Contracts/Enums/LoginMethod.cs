using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>Ways a user can sign in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LoginMethod>))]
public enum LoginMethod
{
    /// <summary>User name and password.</summary>
    UserNamePassword,
    /// <summary>Email and password.</summary>
    EmailPassword,
    /// <summary>Phone number and password.</summary>
    PhonePassword,
    /// <summary>Phone number and one-time code.</summary>
    PhoneOtp,
    /// <summary>Email and one-time code.</summary>
    EmailOtp,
    /// <summary>An external provider (social login, OpenID Connect, SAML).</summary>
    External,
    /// <summary>A directory (LDAP / Active Directory) user name and password.</summary>
    Ldap,
    /// <summary>A passkey (WebAuthn).</summary>
    Passkey,
}
