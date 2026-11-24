using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SystemX.Auth2.Client.Options;

/// <summary>Checks <see cref="Auth2ClientOptions"/> when the application starts and says clearly what is wrong.</summary>
public sealed class Auth2ClientOptionsValidator : IValidateOptions<Auth2ClientOptions>
{
    private static readonly Regex ProjectKeyPattern = new("^[a-z0-9_-]{3,64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>True when the project key follows the server rule: lowercase letters, digits, <c>-</c>, <c>_</c>, 3–64 characters, not <c>_global</c>.</summary>
    public static bool IsValidProjectKey(string? key) =>
        key is not null && key != "_global" && ProjectKeyPattern.IsMatch(key);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, Auth2ClientOptions o)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(o.ServerUrl)) errors.Add("Auth2: ServerUrl is required (for example https://auth.example.com).");
        else if (!Uri.TryCreate(o.ServerUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            errors.Add("Auth2: ServerUrl must be an absolute http or https address.");
        if (string.IsNullOrWhiteSpace(o.ApplicationName)) errors.Add("Auth2: ApplicationName is required. It must be the name of the application on the server.");
        if (string.IsNullOrWhiteSpace(o.ClientId)) errors.Add("Auth2: ClientId is required.");
        if (string.IsNullOrWhiteSpace(o.ClientSecret)) errors.Add("Auth2: ClientSecret is required. Keep it in a secret store, not in the source code.");
        if (o.ProjectKey is not null && !IsValidProjectKey(o.ProjectKey))
            errors.Add("Auth2: ProjectKey may only have lowercase letters, digits, '-' and '_', must be 3 to 64 characters long and must not be '_global'. " +
                       "Leave it out (do not call ServerWithMultiProject) when the server is single-project.");
        if (o.HandshakeTimeout <= TimeSpan.Zero) errors.Add("Auth2: HandshakeTimeout must be longer than zero.");
        if (o.RequestTimeout <= TimeSpan.Zero) errors.Add("Auth2: RequestTimeout must be longer than zero.");
        if (o.HandshakeMaxRetries < 0) errors.Add("Auth2: HandshakeMaxRetries must not be negative.");
        if (o.HandshakeInitialDelay <= TimeSpan.Zero) errors.Add("Auth2: HandshakeInitialDelay must be longer than zero.");
        if (o.HandshakeMaxDelay < o.HandshakeInitialDelay) errors.Add("Auth2: HandshakeMaxDelay must not be shorter than HandshakeInitialDelay.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
