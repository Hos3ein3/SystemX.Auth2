using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Handshake answer. <c>ServerMode</c> tells the client which mode the server uses. <c>ProjectKey</c> is null on a single-project server.</summary>
/// <remarks>
/// <c>WebServerUrl</c> and <c>ApplicationType</c> are what the Server now has for this application (the client's value, or the admin's). <c>WebServerLocked</c> is true when the client sent a
/// web server address or a type that differs from the stored one and the project does not let clients set them: the Server kept the stored values.
/// </remarks>
public sealed record ClientHandshakeResponse(Guid ProjectId, Guid ApplicationId, string? ProjectKey, string ApplicationName,
    ServerMode ServerMode, string Issuer, int SettingsVersion, TimeSpan? CookieLifetime, bool SingleActiveSession,
    string? WebServerUrl = null, ApplicationType? ApplicationType = null, bool WebServerLocked = false);
