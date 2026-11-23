using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>Result of the client handshake.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HandshakeStatus>))]
public enum HandshakeStatus
{
    /// <summary>The handshake worked.</summary>
    Ok,
    /// <summary>The server could not be reached.</summary>
    ServerUnavailable,
    /// <summary>The application was not found.</summary>
    NotFound,
    /// <summary>The server rejected the client (secret, name, key or mode).</summary>
    Rejected,
}
