using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Handshake;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Auth;

namespace SystemX.Auth2.Samples.Mvc.Controllers.Api;

/// <summary>
/// API Authentication controller: exposes current user details, client token metadata, client handshake state, and login endpoints.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthApiController(
    ICurrentUser currentUser,
    ICurrentClient currentClient,
    Auth2ClientState clientState,
    IAuth2ApiClientService apiClientService) : ControllerBase
{
    /// <summary>
    /// Returns the currently authenticated person's profile, roles, permissions, and groups.
    /// </summary>
    [HttpGet("me")]
    [Auth2Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        return Ok(new
        {
            currentUser.IsAuthenticated,
            currentUser.UserId,
            currentUser.UserName,
            currentUser.Email,
            currentUser.PhoneNumber,
            currentUser.FirstName,
            currentUser.LastName,
            currentUser.FullName,
            Roles = await currentUser.GetRolesAsync(ct),
            Permissions = await currentUser.GetPermissionsAsync(ct),
            Groups = await currentUser.GetGroupsAsync(ct)
        });
    }

    /// <summary>
    /// Returns the calling client application details, scopes, machine flag, and authentication context.
    /// </summary>
    [HttpGet("client")]
    [Auth2Authorize]
    public IActionResult GetClient()
    {
        return Ok(new
        {
            currentClient.IsTokenAuthenticated,
            currentClient.ClientId,
            currentClient.ApplicationId,
            currentClient.ProjectId,
            currentClient.ProjectKey,
            currentClient.Scopes,
            currentClient.IsMachine,
            currentClient.SessionId,
            currentClient.Idp,
            currentClient.Amr
        });
    }

    /// <summary>
    /// Returns the client's handshake status with the Auth2 server.
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = clientState.Status?.ToString(),
            clientState.LastResponse?.ServerMode,
            clientState.LastResponse?.Issuer,
            clientState.LastResponse?.CookieLifetime,
            clientState.LastResponse?.SingleActiveSession
        });
    }

    /// <summary>
    /// Authenticates a user directly against the Auth2 Server using credentials.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await apiClientService.LoginAsync(request, ct);
        if (!result.IsSuccess)
        {
            return Problem(
                detail: result.Error?.Message ?? "Authentication failed",
                statusCode: StatusCodes.Status400BadRequest,
                title: result.Error?.Code ?? "invalid_credentials");
        }

        return Ok(result.Value);
    }
}
