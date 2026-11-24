using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>Whether the server serves one project or many.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ServerMode>))]
public enum ServerMode
{
    /// <summary>The server has exactly one project.</summary>
    SingleProject,
    /// <summary>The server serves many projects.</summary>
    MultiProject,
}
