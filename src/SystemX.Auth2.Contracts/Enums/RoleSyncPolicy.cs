using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>How roles move between the client and the server.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RoleSyncPolicy>))]
public enum RoleSyncPolicy
{
    /// <summary>Nothing happens.</summary>
    None,
    /// <summary>Client sends its list to the server.</summary>
    Push,
    /// <summary>Client reads the list from the server.</summary>
    Pull,
    /// <summary>Push first, then pull.</summary>
    Both,
}
