namespace SystemX.Auth2.Contracts.Applications;

/// <summary>
/// Handshake check sent by the client. <c>ProjectKey</c> is null for a single-project server. <c>ApplicationName</c> is the name of the
/// application (the client) inside the project. The body never selects the project: the server finds it from the client id.
/// The project key is only an identifier check, never proof of anything, and it is not a secret.
/// <para>
/// <c>WebServerUrl</c> is the address of the application's web server as the Server reaches it, and <c>ApplicationType</c> says what kind of application it is.
/// The Server stores them on the application <b>only when the application allows it</b> (its setting <c>AllowClientToSetWebServerUrl</c>) and the address is a plain http or https
/// address; otherwise it keeps what an admin set and says so in the answer (<c>WebServerLocked</c>).
/// </para>
/// <para>
/// <b>Two-way health check</b> (the Server asks the client "are you healthy and ready?"): the client says <c>HealthPath</c> (and, when it has no <c>WebServerUrl</c>, the
/// <c>HealthPort</c> and <c>HealthScheme</c> it listens on). The address the Server calls is the <b>web server address of the application plus the path</b>; without an address
/// the Server uses the network address the handshake came from and the port. <c>HealthUrl</c> (the whole address) is how older clients said the same thing. With none of them
/// (a one-way check) the Server does not call the client; a later handshake without them switches the check off again.
/// </para>
/// </summary>
public sealed record ClientHandshakeRequest(string? ProjectKey, string ApplicationName, string ClientVersion, string? HealthUrl = null,
    int? HealthPort = null, string? HealthScheme = null, string? HealthPath = null, string? WebServerUrl = null, Enums.ApplicationType? ApplicationType = null);

/// <summary>
/// What a client answers when the Server asks for its health (<c>GET</c> on the <c>HealthUrl</c> of the handshake). <c>Status</c> is <c>Healthy</c>, <c>Degraded</c> or
/// <c>Unhealthy</c> (the result of all the health checks of the client application). The answer holds no details on purpose.
/// </summary>
public sealed record ClientHealthResponse(string Status, string ApplicationName, string ClientVersion);
