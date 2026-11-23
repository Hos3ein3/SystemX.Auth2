using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>Whether the server serves one application or many.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ServerMode>))]
public enum ServerMode
{
    /// <summary>The server serves one application.</summary>
    SingleApplication,
    /// <summary>The server serves many applications.</summary>
    MultiApplication,
}
