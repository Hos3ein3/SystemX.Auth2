using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>The kind of client application.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClientType>))]
public enum ClientType
{
    /// <summary>A web API.</summary>
    Api,
    /// <summary>An MVC app.</summary>
    Mvc,
    /// <summary>A Razor Pages app.</summary>
    RazorPages,
    /// <summary>A Blazor app.</summary>
    Blazor,
    /// <summary>A background or machine client.</summary>
    Machine,
}
