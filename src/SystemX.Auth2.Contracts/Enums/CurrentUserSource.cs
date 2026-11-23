using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>Where the client reads current user data from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CurrentUserSource>))]
public enum CurrentUserSource
{
    /// <summary>Read only from the token.</summary>
    TokenOnly,
    /// <summary>Read from the token and from the server.</summary>
    TokenAndServer,
}
