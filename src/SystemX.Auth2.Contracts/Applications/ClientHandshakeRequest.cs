namespace SystemX.Auth2.Contracts.Applications;

/// <summary>
/// Handshake check sent by the client. <c>ProjectKey</c> is null for a single-project server. <c>ApplicationName</c> is the name of the
/// application (the client) inside the project. The body never selects the project: the server finds it from the client id.
/// The project key is only an identifier check, never proof of anything, and it is not a secret.
/// <para>
/// <b>Two-way health check</b> (the Server asks the client "are you healthy and ready?"): the client either gives the whole address in <c>HealthUrl</c>
/// (it knows how it is reached from outside), or it only says <c>HealthPort</c>, <c>HealthScheme</c> and <c>HealthPath</c> and the Server takes the host from the
/// network address the handshake came from. With none of them (a one-way check) the Server does not call the client; a later handshake without them switches the check off again.
/// </para>
/// </summary>
public sealed record ClientHandshakeRequest(string? ProjectKey, string ApplicationName, string ClientVersion, string? HealthUrl = null,
    int? HealthPort = null, string? HealthScheme = null, string? HealthPath = null);

/// <summary>
/// What a client answers when the Server asks for its health (<c>GET</c> on the <c>HealthUrl</c> of the handshake). <c>Status</c> is <c>Healthy</c>, <c>Degraded</c> or
/// <c>Unhealthy</c> (the result of all the health checks of the client application). The answer holds no details on purpose.
/// </summary>
public sealed record ClientHealthResponse(string Status, string ApplicationName, string ClientVersion);
