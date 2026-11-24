using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using OpenIddict.Abstractions;

namespace SystemX.Auth2.Server.Endpoints;

/// <summary>
/// The JSON API for client applications: register, one-time codes, password reset and change, confirmations, two-factor, joining.
/// Programs call it with a client-credentials token (<c>auth2.register</c> or <c>auth2.login</c>); a signed-in user calls the password
/// change and two-factor endpoints with their own token (<c>auth2.me</c>).
/// </summary>
public static class AccountEndpoints
{
    /// <summary>Maps the routes of <c>Auth2Routes</c>.</summary>
    public static IEndpointRouteBuilder MapAuth2Account(this IEndpointRouteBuilder routes)
    {
        routes.MapPost(Auth2Routes.Register, RegisterAsync).RequireClient(Auth2Scopes.Register);
        routes.MapPost(Auth2Routes.JoinComplete, async (HttpContext http, JoinCompleteRequest r, IJoinService join) =>
            ApiResults.From(http, await join.CompleteAsync(r.Login, r.Code, r.NewPassword, http.RequestAborted))).RequireClient(Auth2Scopes.Register);

        routes.MapPost(Auth2Routes.OtpRequest, async (HttpContext http, OtpRequest r, IOtpRequestService otp) =>
        {
            var result = await otp.RequestAsync(r.Login, r.Purpose, http.RequestAborted);
            return result.Succeeded ? Results.Accepted() : ApiResults.From(http, result);
        }).RequireClient(Auth2Scopes.Login);

        routes.MapPost(Auth2Routes.PasswordForgot, async (HttpContext http, ForgotPasswordRequest r, IPasswordService passwords, IReturnUrlValidator urls, IAuthFlowResolver flow) =>
        {
            await passwords.ForgotAsync(r.Login, await SafeReturnUrlAsync(r.ReturnUrl, urls, flow, http), http.RequestAborted);
            return Results.Accepted();   // always the same answer
        }).RequireClient(Auth2Scopes.Login);

        routes.MapPost(Auth2Routes.PasswordReset, async (HttpContext http, ResetPasswordRequest r, IPasswordService passwords) =>
            ApiResults.From(http, await passwords.ResetAsync(r.Login, r.Token, r.NewPassword, http.RequestAborted))).RequireClient(Auth2Scopes.Login);

        routes.MapPost(Auth2Routes.EmailConfirm, async (HttpContext http, ConfirmEmailRequest r, IConfirmationService c) =>
            ApiResults.From(http, await c.ConfirmEmailAsync(r.UserId, r.Token, http.RequestAborted))).RequireClient(Auth2Scopes.Login);

        routes.MapPost(Auth2Routes.EmailConfirmResend, async (HttpContext http, ResendEmailConfirmationRequest r, IConfirmationService c, IReturnUrlValidator urls, IAuthFlowResolver flow) =>
        {
            await c.ResendEmailAsync(r.Login, await SafeReturnUrlAsync(r.ReturnUrl, urls, flow, http), http.RequestAborted);
            return Results.Accepted();
        }).RequireClient(Auth2Scopes.Login);

        routes.MapPost(Auth2Routes.PhoneConfirm, async (HttpContext http, ConfirmPhoneRequest r, IConfirmationService c) =>
            ApiResults.From(http, await c.ConfirmPhoneAsync(r.Login, r.Code, http.RequestAborted))).RequireClient(Auth2Scopes.Login);

        // ---- signed-in user
        routes.MapPost(Auth2Routes.PasswordChange, async (HttpContext http, ChangePasswordRequest r, IPasswordService passwords) =>
            ApiResults.From(http, await passwords.ChangeAsync(http.CurrentUser(), r.CurrentPassword, r.NewPassword, http.User.GetAuthorizationId(), http.RequestAborted))).RequireUser(Auth2Scopes.Me);

        routes.MapPost(Auth2Routes.TwoFactorSetup, async (HttpContext http, ITwoFactorService tf) =>
        {
            var (result, setup) = await tf.SetupAsync(http.CurrentUser(), http.RequestAborted);
            return result.Succeeded ? Results.Ok(setup) : ApiResults.From(http, result);
        }).RequireUser(Auth2Scopes.Me);

        routes.MapPost(Auth2Routes.TwoFactorEnable, async (HttpContext http, TwoFactorEnableRequest r, ITwoFactorService tf) =>
        {
            var (result, codes) = await tf.EnableAsync(http.CurrentUser(), r.Code, http.RequestAborted);
            return result.Succeeded ? Results.Ok(new TwoFactorEnableResponse(codes)) : ApiResults.From(http, result);
        }).RequireUser(Auth2Scopes.Me);

        routes.MapPost(Auth2Routes.TwoFactorDisable, async (HttpContext http, TwoFactorDisableRequest r, ITwoFactorService tf) =>
            ApiResults.From(http, await tf.DisableAsync(http.CurrentUser(), r.Password, http.RequestAborted))).RequireUser(Auth2Scopes.Me);

        routes.MapPost(Auth2Routes.TwoFactorRecoveryCodes, async (HttpContext http, RecoveryCodesRequest r, ITwoFactorService tf) =>
        {
            var (result, codes) = await tf.RegenerateRecoveryCodesAsync(http.CurrentUser(), r.Code, http.RequestAborted);
            return result.Succeeded ? Results.Ok(new RecoveryCodesResponse(codes)) : ApiResults.From(http, result);
        }).RequireUser(Auth2Scopes.Me);

        // The user comes from the token only. This route never takes a user id.
        routes.MapGet(Auth2Routes.Me, async (HttpContext http, IAccessService access) =>
            Results.Ok(await access.GetCurrentUserAsync(http.CurrentUser(), http.User.GetScopes(), http.RequestAborted))).RequireUser(Auth2Scopes.Me);

        return routes;
    }

    private static async Task<IResult> RegisterAsync(HttpContext http, RegisterRequest r, IRegisterService register, IReturnUrlValidator urls, IAuthFlowResolver flow)
    {
        var outcome = await register.RegisterAsync(r, await SafeReturnUrlAsync(r.ReturnUrl, urls, flow, http), http.RequestAborted);
        if (!outcome.Result.Succeeded) return ApiResults.From(http, outcome.Result);
        return Results.Ok(new RegisterResponse(outcome.UserId ?? Guid.Empty, outcome.UserWasCreated, outcome.JoinedProject, outcome.ConfirmationRequired));
    }

    /// <summary>A return URL goes into emails, so it must pass the same checks as every redirect. Otherwise it is dropped.</summary>
    private static async Task<string?> SafeReturnUrlAsync(string? returnUrl, IReturnUrlValidator urls, IAuthFlowResolver flow, HttpContext http)
    {
        if (string.IsNullOrEmpty(returnUrl)) return null;
        var settings = await flow.GetEffectiveAsync(http.RequestAborted);
        return urls.IsAllowed(returnUrl, settings.AllowedRedirectUris) ? returnUrl : null;
    }
}
