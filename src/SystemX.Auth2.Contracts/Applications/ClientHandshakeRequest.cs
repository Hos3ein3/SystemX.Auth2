namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Handshake check sent by the client. <c>ProjectKey</c> is null for a single-application server. The body never selects the application.</summary>
public sealed record ClientHandshakeRequest(string? ProjectKey, string ApplicationName, string ClientVersion);
