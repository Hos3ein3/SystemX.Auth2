using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>Who shows the login screens.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UiMode>))]
public enum UiMode
{
    /// <summary>Nothing happens.</summary>
    None,
    /// <summary>The server shows the login pages.</summary>
    Hosted,
    /// <summary>The client app shows its own login pages.</summary>
    Self,
}
