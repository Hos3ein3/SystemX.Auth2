using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Server.Application;

/// <summary>Tokens of the upstream provider. Only filled when <c>StoreProviderTokens</c> is on.</summary>
public sealed record ExternalProviderTokens(string? AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// What an external source tells the Server about a person, after the Server checked the answer.
/// <c>Attributes</c> are kept in memory only: they are never stored or logged as a whole.
/// </summary>
public sealed record ExternalIdentity(
    string Provider, string Subject, string? Issuer, string? Email, bool? EmailVerified,
    string? FirstName, string? LastName, string? Phone, IReadOnlyList<string> Groups,
    IReadOnlyDictionary<string, string[]> Attributes)
{
    /// <summary>Display name, when the source gives one.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Picture URL, when the source gives one.</summary>
    public string? Picture { get; init; }
    /// <summary>The source says the user signed in with several factors (used with <c>TrustProviderMfa</c>).</summary>
    public bool? UsedMultiFactor { get; init; }
    /// <summary>Upstream tokens (only when the provider setting asks for them).</summary>
    public ExternalProviderTokens? Tokens { get; init; }
}

/// <summary>A failed external sign-in. <c>ErrorCode</c> is a stable code of <c>Auth2ErrorCodes</c>. <c>Reason</c> is a short safe word for the audit log (never user data).</summary>
public class ExternalLoginException(string errorCode, string reason, Exception? inner = null) : Exception($"{errorCode}: {reason}", inner)
{
    /// <summary>Stable error code.</summary>
    public string ErrorCode { get; } = errorCode;
    /// <summary>Short reason for the audit log.</summary>
    public string Reason { get; } = reason;
}

/// <summary>Input of a challenge (the start of a redirect sign-in).</summary>
/// <param name="Provider">The provider.</param>
/// <param name="Issuer">Public address of this Server.</param>
/// <param name="ReturnUrl">Where to go afterwards.</param>
/// <param name="LoginHint">Email or name to prefill at the provider.</param>
/// <param name="LinkUserId">Set when a signed-in user links the provider instead of signing in.</param>
public sealed record ExternalChallengeContext(RegisteredProvider Provider, string Issuer, string ReturnUrl, string? LoginHint, string? LinkUserId = null);

/// <summary>Where to send the browser, and the protected state the endpoint must keep in a short-lived cookie until the callback.</summary>
public sealed record ExternalChallenge(Uri RedirectUri, string ProtectedState, TimeSpan StateLifetime)
{
    /// <summary>When the provider needs a browser form post (SAML POST binding), the data for the form. <see cref="RedirectUri"/> is then the form action.</summary>
    public IReadOnlyDictionary<string, string>? PostFields { get; init; }
}

/// <summary>Input of a callback. <c>Parameters</c> are the query (GET) or form (POST) values.</summary>
public sealed record ExternalCallbackContext(RegisteredProvider Provider, string Issuer, IReadOnlyDictionary<string, string> Parameters, string? ProtectedState);

/// <summary>Result of a callback: the identity and the return URL that was chosen at the start.</summary>
public sealed record ExternalCallbackResult(ExternalIdentity Identity, string ReturnUrl, string? LinkUserId = null)
{
    /// <summary>The provider started this sign-in (SAML IdP-initiated). The return URL is then never taken from the provider.</summary>
    public bool IdpInitiated { get; init; }
}

/// <summary>A source of external identities.</summary>
public interface IExternalIdentitySource
{
    /// <summary>The types this source handles.</summary>
    bool Handles(ExternalProviderType type);
}

/// <summary>A source that sends the browser away and takes it back (OIDC, SAML).</summary>
public interface IRedirectIdentitySource : IExternalIdentitySource
{
    /// <summary>Starts the sign-in.</summary>
    Task<ExternalChallenge> ChallengeAsync(ExternalChallengeContext ctx, CancellationToken ct);
    /// <summary>Checks the answer of the provider.</summary>
    Task<ExternalCallbackResult> AuthenticateCallbackAsync(ExternalCallbackContext ctx, CancellationToken ct);
}

/// <summary>Remembers values that may be used only once (login state, SAML ids). Shared by all instances through the database.</summary>
public interface IReplayCache
{
    /// <summary>Adds the key. Returns false when it was already there (a replay).</summary>
    Task<bool> TryAddAsync(Guid applicationId, string key, DateTimeOffset expiresAtUtc, CancellationToken ct = default);
}
