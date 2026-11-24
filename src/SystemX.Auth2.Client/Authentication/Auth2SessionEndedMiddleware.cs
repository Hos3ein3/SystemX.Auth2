using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Contracts.Constants;

namespace SystemX.Auth2.Client.Authentication;

/// <summary>
/// The user signed out (or was signed out) on the Auth2 server, but this application still has its own cookie. The first call that asks the
/// server for the user's access data then fails with <c>invalid_token</c>. That is not an error of the application: the session is over.
/// This middleware removes the cookie and sends a browser to sign in again (an API or AJAX call gets 401).
/// It only reacts when nothing was written to the response yet; any other <see cref="Auth2Exception"/> is left alone.
/// </summary>
internal sealed class Auth2SessionEndedMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (Auth2Exception ex) when (ex.ErrorCode == Auth2ErrorCodes.InvalidToken && !http.Response.HasStarted)
        {
            if (await http.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(Auth2AuthSchemes.Cookie) is not null)
                await http.SignOutAsync(Auth2AuthSchemes.Cookie);

            var web = http.RequestServices.GetService<IAuth2WebService>();
            if (Auth2Http.IsApiRequest(http) || web is null)
            {
                http.Response.Clear();
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            http.Response.Clear();
            await web.ChallengeAsync(http, http.Request.Path + http.Request.QueryString);
        }
    }
}
