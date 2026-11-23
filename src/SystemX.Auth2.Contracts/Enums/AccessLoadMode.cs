using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>When the client loads access data (roles and permissions).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccessLoadMode>))]
public enum AccessLoadMode
{
    /// <summary>Load on first use.</summary>
    Lazy,
    /// <summary>Load when the user signs in.</summary>
    OnSignIn,
    /// <summary>Put roles in the cookie.</summary>
    RolesInCookie,
    /// <summary>Nothing happens.</summary>
    None,
}
