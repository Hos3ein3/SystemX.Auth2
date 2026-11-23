using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Handshake answer. <c>ServerMode</c> tells the client which mode the server uses.</summary>
public sealed record ClientHandshakeResponse(Guid ApplicationId, string? ProjectKey, string ApplicationName,
    ServerMode ServerMode, string Issuer, int SettingsVersion, TimeSpan? CookieLifetime, bool SingleActiveSession);
