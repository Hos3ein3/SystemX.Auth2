using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Handshake answer. <c>ServerMode</c> tells the client which mode the server uses. <c>ProjectKey</c> is null on a single-project server.</summary>
public sealed record ClientHandshakeResponse(Guid ProjectId, Guid ApplicationId, string? ProjectKey, string ApplicationName,
    ServerMode ServerMode, string Issuer, int SettingsVersion, TimeSpan? CookieLifetime, bool SingleActiveSession);
