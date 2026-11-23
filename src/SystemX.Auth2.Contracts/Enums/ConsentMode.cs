using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>How the consent screen is used.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConsentMode>))]
public enum ConsentMode
{
    /// <summary>No consent screen for trusted clients.</summary>
    Implicit,
    /// <summary>Ask for consent once.</summary>
    Explicit,
    /// <summary>Always ask for consent.</summary>
    Systematic,
}
