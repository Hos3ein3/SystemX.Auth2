using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SystemX.Auth2.Server.Endpoints;

/// <summary>
/// The OpenIddict endpoints in passthrough mode: OpenIddict parses and validates the request, these handlers run our rules
/// (<see cref="ILoginService"/>) and call sign-in. There is no other path that issues tokens.
/// </summary>
public static class ConnectEndpoints
{
    private static readonly string OpenIddictScheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;

    /// <summary>Maps /connect/authorize, token, endsession and userinfo.</summary>
    public static IEndpointRouteBuilder MapAuth2Connect(this IEndpointRouteBuilder routes)
    {
        routes.MapMethods(Auth2Routes.Authorize, [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).ExcludeFromDescription();
        routes.MapPost(Auth2Routes.Token, TokenAsync).ExcludeFromDescription();
        routes.MapMethods(Auth2Routes.EndSession, [HttpMethods.Get, HttpMethods.Post], EndSessionAsync).ExcludeFromDescription();
        routes.MapMethods(Auth2Routes.UserInfo, [HttpMethods.Get, HttpMethods.Post], (Delegate)UserInfoAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = OpenIddictScheme }).ExcludeFromDescription();
        return routes;
    }

    // ---------------------------------------------------------------- authorize

    private static async Task<IResult> AuthorizeAsync(HttpContext http, IClientIdAccessor clientId, IAuthFlowResolver flow,
        UserManager<AppUser> users, ITokenIssuer issuer, ILoginService login, IConsentService consent)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        clientId.ClientId = request.ClientId;
        var app = await flow.GetApplicationAsync(http.RequestAborted);

        var cookie = await http.AuthenticateAsync(Auth2Cookie.Scheme);
        var wrongApplication = cookie.Succeeded && cookie.Principal!.FindFirst(Auth2Claims.ApplicationId)?.Value != app.Id.ToString("D");
        var mustLogin = !cookie.Succeeded || wrongApplication || request.HasPromptValue(PromptValues.Login);

        AppUser? user = null;
        if (!mustLogin)
        {
            user = await users.FindByIdAsync(cookie.Principal!.FindFirst("sub")!.Value);
            if (user is null || !await login.IsSubjectValidAsync(user, http.RequestAborted)) mustLogin = true;
        }

        if (mustLogin)
        {
            if (request.HasPromptValue(PromptValues.None))
                return Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not signed in.",
                }), [OpenIddictScheme]);

            // Come back here after the login page. "prompt=login" is removed, or we would loop forever.
            return Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrl(http, Parameters.Prompt) }, [Auth2Cookie.Scheme]);
        }

        // Consent: none (implicit), once per user and scopes (explicit), or every time (systematic).
        var settings = await flow.GetSettingsAsync(http.RequestAborted);
        var scopes = request.GetScopes();
        var mode = ConsentPolicy.Effective(settings);
        string? authorizationId = null;
        if (mode != ConsentMode.Implicit)
        {
            if (await consent.IsConsentNeededAsync(user!, app, settings, scopes, (string?)request["consent"], http.RequestAborted))
            {
                if (request.HasPromptValue(PromptValues.None))
                    return Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user must agree first.",
                    }), [OpenIddictScheme]);
                // The consent page gets the authorize URL (without prompt and without an old consent token) and comes back to it.
                var back = ReturnUrl(http, Parameters.Prompt, "consent");
                return Results.Redirect($"/Account/Consent?ClientId={Uri.EscapeDataString(app.ClientId)}&ReturnUrl={Uri.EscapeDataString(back)}");
            }
            if (mode == ConsentMode.Explicit) authorizationId = await consent.FindAuthorizationIdAsync(user!, app, scopes, http.RequestAborted);
        }

        var amr = cookie.Principal!.FindAll(Auth2Claims.Amr).Select(c => c.Value).DefaultIfEmpty("pwd");
        var principal = await issuer.CreateUserPrincipalAsync(user!, scopes, amr, cookie.Principal.FindFirst(Auth2Claims.SessionId)?.Value, http.RequestAborted);
        if (mode == ConsentMode.Explicit)
        {
            // Remember the agreement, so the user is not asked again for the same scopes. The tokens belong to this authorization.
            authorizationId ??= await consent.SaveAuthorizationAsync(user!, app, principal, scopes, http.RequestAborted);
            principal.SetAuthorizationId(authorizationId);
        }
        return Results.SignIn(principal, properties: null, OpenIddictScheme);
    }

    /// <summary>The URL of the current authorize request, without the listed parameters.</summary>
    private static string ReturnUrl(HttpContext http, params string[] without)
    {
        var parameters = http.Request.HasFormContentType ? http.Request.Form.ToList() : http.Request.Query.ToList();
        return http.Request.PathBase + http.Request.Path + QueryString.Create(parameters.Where(p => !without.Contains(p.Key)));
    }

    // ---------------------------------------------------------------- token

    private static async Task<IResult> TokenAsync(HttpContext http, IClientIdAccessor clientId, IAuthFlowResolver flow, UserManager<AppUser> users,
        ITokenIssuer issuer, ILoginService login)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        clientId.ClientId = request.ClientId;
        var ct = http.RequestAborted;
        var app = await flow.GetApplicationAsync(ct);
        var settings = await flow.GetSettingsAsync(ct);
        http.Response.Headers.CacheControl = "no-store";

        if (!app.IsEnabled) return OAuthError(Errors.InvalidGrant, Auth2ErrorCodes.ApplicationDisabled);

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var auth = await http.AuthenticateAsync(OpenIddictScheme);
            var principal = auth.Principal!;
            var user = await users.FindByIdAsync(principal.GetClaim(Claims.Subject) ?? "");
            if (user is null || !await login.IsSubjectValidAsync(user, ct))
                return OAuthError(Errors.InvalidGrant, Auth2ErrorCodes.InvalidCredentials);

            if (request.IsRefreshTokenGrantType()) await issuer.RefreshUserClaimsAsync(principal, user, ct);
            return Results.SignIn(principal, properties: null, OpenIddictScheme);
        }

        if (request.IsClientCredentialsGrantType())
            return Results.SignIn(await issuer.CreateMachinePrincipalAsync(request.GetScopes(), ct), properties: null, OpenIddictScheme);

        if (request.IsPasswordGrantType())
        {
            // OpenIddict already checked that this client has the password permission (only given when AllowSelfUiLogin is true).
            // The client is confidential: it was authenticated with its secret before we got here. We check the setting again.
            if (!settings.Clients.AllowSelfUiLogin) return OAuthError(Errors.UnauthorizedClient, Auth2ErrorCodes.GrantNotAllowed);

            var result = await login.PasswordLoginAsync(new PasswordLoginRequest(request.Username ?? "", request.Password ?? "",
                http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString()), ct);
            return await ToTokenResultAsync(result, request, issuer, ct);
        }

        if (request.GrantType == Auth2GrantTypes.TwoFactor)
        {
            if (!settings.Clients.AllowSelfUiLogin) return OAuthError(Errors.UnauthorizedClient, Auth2ErrorCodes.GrantNotAllowed);
            var result = await login.CompleteTwoFactorAsync((string?)request["two_factor_token"] ?? "", (string?)request["code"], (string?)request["recovery_code"],
                http.Connection.RemoteIpAddress?.ToString(), ct);
            return await ToTokenResultAsync(result, request, issuer, ct);
        }

        if (request.GrantType == Auth2GrantTypes.Otp)
        {
            if (!settings.Clients.AllowSelfUiLogin) return OAuthError(Errors.UnauthorizedClient, Auth2ErrorCodes.GrantNotAllowed);
            var result = await login.OtpLoginAsync(new OtpLoginRequest(request.Username ?? "", (string?)request["code"] ?? "",
                http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString()), ct);
            return await ToTokenResultAsync(result, request, issuer, ct);
        }

        return OAuthError(Errors.UnsupportedGrantType, Auth2ErrorCodes.GrantNotAllowed);
    }

    private static async Task<IResult> ToTokenResultAsync(LoginResult result, OpenIddictRequest request, ITokenIssuer issuer, CancellationToken ct)
    {
        if (result.Succeeded)
        {
            var principal = await issuer.CreateUserPrincipalAsync(result.User!, request.GetScopes(), result.Amr, result.SessionId, ct);
            return Results.SignIn(principal, properties: null, OpenIddictScheme);
        }
        if (result.ErrorCode == Auth2ErrorCodes.TwoFactorRequired)
            return OAuthError(Auth2ErrorCodes.TwoFactorRequired, Auth2ErrorCodes.TwoFactorRequired, result.TwoFactorToken);
        return OAuthError(Errors.InvalidGrant, result.ErrorCode ?? Auth2ErrorCodes.InvalidCredentials);
    }

    /// <summary>Standard OAuth <c>error</c> + <c>error_description</c>, plus our stable <c>error_code</c>.</summary>
    private static IResult OAuthError(string error, string code, string? twoFactorToken = null) =>
        Results.Json(new Dictionary<string, string?>
        {
            ["error"] = error,
            ["error_description"] = code.Replace('_', ' '),
            ["error_code"] = code,
            ["two_factor_token"] = twoFactorToken,
        }.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value), statusCode: StatusCodes.Status400BadRequest);

    // ---------------------------------------------------------------- end session

    private static async Task<IResult> EndSessionAsync(HttpContext http, IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls)
    {
        var request = http.GetOpenIddictServerRequest();
        await http.SignOutAsync(Auth2Cookie.Scheme);

        // post_logout_redirect_uri was already checked by OpenIddict (exact match with the registered list) and is used by it.
        // Without one, we go to RedirectAfterLogout of the application, then to the home page.
        var fallback = "/";
        if (!string.IsNullOrEmpty(request?.ClientId))
        {
            clientId.ClientId = request.ClientId;
            var settings = await flow.GetSettingsAsync(http.RequestAborted);
            fallback = urls.Resolve(null, settings.Redirects.RedirectAfterLogout, settings);
        }
        return Results.SignOut(new AuthenticationProperties { RedirectUri = fallback }, [OpenIddictScheme]);
    }

    // ---------------------------------------------------------------- userinfo

    private static async Task<IResult> UserInfoAsync(HttpContext http)
    {
        var principal = (await http.AuthenticateAsync(OpenIddictScheme)).Principal!;
        var claims = new Dictionary<string, object?> { [Claims.Subject] = principal.GetClaim(Claims.Subject) };

        void Add(string scope, params string[] types)
        {
            if (!principal.HasScope(scope)) return;
            foreach (var t in types) if (principal.GetClaim(t) is { } v) claims[t] = v;
        }
        Add(Scopes.Profile, Claims.Name, Claims.GivenName, Claims.FamilyName, Claims.PreferredUsername, Auth2Claims.Locale);
        Add(Scopes.Email, Claims.Email, Claims.EmailVerified);
        Add(Scopes.Phone, Claims.PhoneNumber);
        if (principal.HasScope(Scopes.Roles)) claims[Claims.Role] = principal.GetClaims(Claims.Role);
        return Results.Ok(claims);
    }
}
