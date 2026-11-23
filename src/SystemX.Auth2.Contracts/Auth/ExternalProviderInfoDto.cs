using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Contracts.Auth;

/// <summary>An external sign-in choice for the login page of an application.</summary>
/// <param name="Name">Provider name inside the application (lowercase, used in URLs).</param>
/// <param name="DisplayName">Text for the button.</param>
/// <param name="Kind">The kind of provider.</param>
/// <param name="ChallengeUrl">Where to send the user (a POST form on the server) for redirect-based providers. Null for LDAP.</param>
/// <param name="LoginFormRequired">True for LDAP: the user types a user name and password.</param>
public sealed record ExternalProviderInfoDto(string Name, string DisplayName, ExternalProviderType Kind, string? ChallengeUrl, bool LoginFormRequired);

/// <summary>Answer of <c>GET /connect/external/providers</c>.</summary>
/// <param name="Providers">The enabled providers of the application.</param>
/// <param name="SuggestedProvider">Home-realm discovery: the provider that matches the <c>hint</c> (an email domain or a provider name), or null.</param>
public sealed record ExternalProvidersResponse(IReadOnlyList<ExternalProviderInfoDto> Providers, string? SuggestedProvider);
