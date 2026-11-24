using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Application;

/// <summary>Result of a passkey operation with a value (the JSON for the browser, or the new passkey).</summary>
public sealed record PasskeyOutcome<T>(OperationResult Result, T? Value = default)
{
    /// <summary>True when it worked.</summary>
    public bool Succeeded => Result.Succeeded;
    /// <summary>Success with a value.</summary>
    public static PasskeyOutcome<T> Ok(T value) => new(OperationResult.Ok, value);
    /// <summary>Failure with an error code.</summary>
    public static PasskeyOutcome<T> Fail(string code) => new(OperationResult.Fail(code));
}

/// <summary>
/// Passkeys (WebAuthn) as a primary way to sign in, per application. Built on the ASP.NET Core Identity passkey support.
/// The per-application rules (allowed, limits, user verification, backup rule, blocked authenticators) are checked here.
/// </summary>
public interface IPasskeyService
{
    /// <summary>Starts a registration: the <c>PublicKeyCredentialCreationOptions</c> JSON for <c>navigator.credentials.create</c>.</summary>
    Task<PasskeyOutcome<string>> CreationOptionsAsync(AppUser user, CancellationToken ct = default);
    /// <summary>Finishes a registration with the JSON the browser made.</summary>
    Task<PasskeyOutcome<PasskeyDto>> RegisterAsync(AppUser user, string credentialJson, string? name, CancellationToken ct = default);
    /// <summary>Starts a sign-in: the <c>PublicKeyCredentialRequestOptions</c> JSON. With a login name the options name that user's passkeys; without (null) the browser may offer any passkey (usernameless, conditional UI).</summary>
    Task<PasskeyOutcome<string>> RequestOptionsAsync(string? login, CancellationToken ct = default);
    /// <summary>Finishes a sign-in with the JSON the browser made. Goes through the one login pipeline.</summary>
    Task<LoginResult> SignInAsync(string credentialJson, string? ipAddress, string? userAgent, CancellationToken ct = default);
    /// <summary>The passkeys of a user in the current application.</summary>
    Task<IReadOnlyList<PasskeyDto>> ListAsync(AppUser user, CancellationToken ct = default);
    /// <summary>Renames a passkey.</summary>
    Task<OperationResult> RenameAsync(AppUser user, string id, string name, CancellationToken ct = default);
    /// <summary>Removes a passkey. The last way to sign in cannot be removed.</summary>
    Task<OperationResult> RemoveAsync(AppUser user, string id, CancellationToken ct = default);
}
