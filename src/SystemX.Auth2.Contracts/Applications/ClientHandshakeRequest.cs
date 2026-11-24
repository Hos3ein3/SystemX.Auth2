namespace SystemX.Auth2.Contracts.Applications;

/// <summary>
/// Handshake check sent by the client. <c>ProjectKey</c> is null for a single-project server. <c>ApplicationName</c> is the name of the
/// application (the client) inside the project. The body never selects the project: the server finds it from the client id.
/// The project key is only an identifier check, never proof of anything, and it is not a secret.
/// </summary>
public sealed record ClientHandshakeRequest(string? ProjectKey, string ApplicationName, string ClientVersion);
