using System.Text.Json.Serialization;

namespace SystemX.Auth2.Contracts.Enums;

/// <summary>
/// What kind of program an application (a client of a project) is. It is a description for admins and tools: it does not change what the application may do
/// (the scopes, roles and permissions do that). A client tells it in the handshake (the Client package works it out from what you configured), and an admin can set it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApplicationType>))]
public enum ApplicationType
{
    /// <summary>Not said. The value of an application that nobody has described.</summary>
    Other,
    /// <summary>A web application with pages for people: MVC, Razor Pages, Blazor. It has a web server and a cookie sign-in.</summary>
    Web,
    /// <summary>A web API: it answers requests with a bearer token and shows no pages.</summary>
    Api,
    /// <summary>A service or background job (a worker): no web server, no person. It works as a program (machine to machine).</summary>
    Service,
}
