using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>How permissions move between the client and the server.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PermissionSyncPolicy>))]
public enum PermissionSyncPolicy
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
