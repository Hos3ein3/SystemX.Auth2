namespace SystemX.Auth2.Client;

/// <summary>Names used by the client. Tests and advanced setups can configure the named HTTP clients with them.</summary>
public static class Auth2ClientDefaults
{
    /// <summary>Name of the HTTP client for calls that must not be repeated automatically (register, login, password change).</summary>
    public const string HttpClientName = "SystemX.Auth2";

    /// <summary>Name of the HTTP client for calls that can safely be repeated (token, handshake, sync, reads). Gets the resilience handler with <c>EnableResilience()</c>.</summary>
    public const string ResilientHttpClientName = "SystemX.Auth2.Resilient";
}
