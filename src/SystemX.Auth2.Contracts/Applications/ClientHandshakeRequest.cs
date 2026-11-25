namespace SystemX.Auth2.Contracts.Applications;

/// <summary>
/// Handshake check sent by the client. <c>ProjectKey</c> is null for a single-project server. <c>ApplicationName</c> is the name of the
/// application (the client) inside the project. The body never selects the project: the server finds it from the client id.
/// The project key is only an identifier check, never proof of anything, and it is not a secret.
/// <c>HealthUrl</c> is the address where the Server may ask the client "are you healthy and ready?" (two-way health check). It is <c>null</c> for a one-way
/// check, and then the Server does not call the client; sending <c>null</c> in a later handshake switches the check off again.
/// </summary>
public sealed record ClientHandshakeRequest(string? ProjectKey, string ApplicationName, string ClientVersion, string? HealthUrl = null);

/// <summary>
/// What a client answers when the Server asks for its health (<c>GET</c> on the <c>HealthUrl</c> of the handshake). <c>Status</c> is <c>Healthy</c>, <c>Degraded</c> or
/// <c>Unhealthy</c> (the result of all the health checks of the client application). The answer holds no details on purpose.
/// </summary>
public sealed record ClientHealthResponse(string Status, string ApplicationName, string ClientVersion);
