using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>
/// How groups move between the client and the server. Pushing groups is off on the server unless the application allows it
/// (<c>Clients.GroupSyncAllowed</c>), because admins normally decide groups. Push only adds missing groups; it never changes or removes admin work.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GroupSyncPolicy>))]
public enum GroupSyncPolicy
{
    /// <summary>Nothing happens.</summary>
    None,
    /// <summary>Client reads the list from the server.</summary>
    Pull,
    /// <summary>Client sends its list to the server (adds missing groups only).</summary>
    Push,
    /// <summary>Push first, then pull.</summary>
    Both,
}
