using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>Where the client finds permissions and roles.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScanningPolicy>))]
public enum ScanningPolicy
{
    /// <summary>Only the lists written in options.</summary>
    Manual,
    /// <summary>Find items from endpoints and attributes.</summary>
    AssemblyScan,
    /// <summary>Push first, then pull.</summary>
    Both,
}
