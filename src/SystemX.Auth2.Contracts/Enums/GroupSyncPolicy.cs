using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>How groups move between the client and the server. Admins decide groups, so there is no push.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GroupSyncPolicy>))]
public enum GroupSyncPolicy
{
    /// <summary>Nothing happens.</summary>
    None,
    /// <summary>Client reads the list from the server.</summary>
    Pull,
}
