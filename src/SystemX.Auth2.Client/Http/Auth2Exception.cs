namespace SystemX.Auth2.Client;

/// <summary>Stable error codes the client itself makes (the server codes are in <c>Auth2ErrorCodes</c>).</summary>
public static class Auth2ClientErrorCodes
{
    /// <summary>The server could not be reached, or answered with a 5xx error, or was too slow.</summary>
    public const string ServerUnavailable = "server_unavailable";

    /// <summary>The answer of the server could not be understood.</summary>
    public const string InvalidResponse = "invalid_response";
}

/// <summary>
/// Error of a low-level building block (token provider, token handler, start-up). The services (<c>IAuth2ApiClientService</c>) never throw it:
/// they return <c>Result</c> / <c>Result&lt;T&gt;</c>. It never holds secrets.
/// </summary>
public sealed class Auth2Exception(int? statusCode, string errorCode, string message, bool isTransient = false, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>HTTP status of the answer, or null when there was none (network error, timeout).</summary>
    public int? StatusCode { get; } = statusCode;

    /// <summary>Stable error code.</summary>
    public string ErrorCode { get; } = errorCode;

    /// <summary>True when trying again later may work (network error, timeout, 5xx).</summary>
    public bool IsTransient { get; } = isTransient;
}
