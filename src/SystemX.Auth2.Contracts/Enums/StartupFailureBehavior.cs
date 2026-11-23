using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>What the client does when the startup handshake fails.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StartupFailureBehavior>))]
public enum StartupFailureBehavior
{
    /// <summary>Stop the app with an exception.</summary>
    Throw,
    /// <summary>Log a warning and keep running.</summary>
    Continue,
}
