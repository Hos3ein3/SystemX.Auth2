using Microsoft.Extensions.Options;

namespace SystemX.Auth2.Client.Options;

/// <summary>Who asks whom in the health check ([EnableHealthCheck]).</summary>
public enum Auth2HealthCheckMode
{
    /// <summary>
    /// The client checks the server: the health check <c>auth2</c> is <c>Healthy</c> after a good handshake and <c>Degraded</c> otherwise. The server never calls the client.
    /// </summary>
    OneWay,

    /// <summary>
    /// <see cref="OneWay"/>, and the server also checks the client: the client tells the server where it answers (<see cref="Auth2HealthCheckOptions.PublicUrl"/> and
    /// <see cref="Auth2HealthCheckOptions.Path"/>) and the server asks every minute whether the client is healthy and ready. The answer is shown in the admin console.
    /// </summary>
    TwoWay,
}

/// <summary>Options of <c>EnableHealthCheck</c>.</summary>
public sealed class Auth2HealthCheckOptions
{
    /// <summary>Who asks whom. Default <see cref="Auth2HealthCheckMode.OneWay"/>.</summary>
    public Auth2HealthCheckMode Mode { get; set; } = Auth2HealthCheckMode.OneWay;

    /// <summary>
    /// The address of <b>this application</b> as the Server reaches it (for example <c>https://shop.example.com</c>, or <c>http://sample-mvc:5001</c> inside docker).
    /// Required for <see cref="Auth2HealthCheckMode.TwoWay"/>: an absolute http or https address without a user name, a query or a fragment.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>The path where this application answers the health question. Default <c>/auth2/health</c>. Used by <see cref="Auth2HealthCheckMode.TwoWay"/>.</summary>
    public string Path { get; set; } = "/auth2/health";

    /// <summary>The full address the Server is told to call, or null for a one-way check.</summary>
    public string? HealthUrl => Mode == Auth2HealthCheckMode.TwoWay && !string.IsNullOrWhiteSpace(PublicUrl) ? PublicUrl.TrimEnd('/') + Path : null;
}

/// <summary>Checks <see cref="Auth2HealthCheckOptions"/> when the application starts.</summary>
public sealed class Auth2HealthCheckOptionsValidator : IValidateOptions<Auth2HealthCheckOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, Auth2HealthCheckOptions o)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(o.Path) || !o.Path.StartsWith('/') || o.Path.Contains('?') || o.Path.Contains('#'))
            errors.Add("Auth2: the health check Path must start with '/' and have no query or fragment (for example /auth2/health).");
        if (o.Mode == Auth2HealthCheckMode.TwoWay)
        {
            if (string.IsNullOrWhiteSpace(o.PublicUrl)) errors.Add("Auth2: a two-way health check needs PublicUrl: the address of this application as the Auth2 server reaches it (for example https://shop.example.com).");
            else if (!Uri.TryCreate(o.PublicUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                     || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                errors.Add("Auth2: the health check PublicUrl must be an absolute http or https address without a user name, a query or a fragment.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
