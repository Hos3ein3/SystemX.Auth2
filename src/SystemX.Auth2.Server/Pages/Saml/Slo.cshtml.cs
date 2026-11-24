#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Pages.Saml;

/// <summary>
/// SAML single logout, started by the identity provider. Only when <c>AllowSingleLogout</c> is on. The request must be signed.
/// The user is signed out of the Server (cookie and tokens of this application) and the identity provider gets a LogoutResponse.
/// The Server never calls the identity provider to log out (logout of the Server is always local).
/// </summary>
public sealed class SloModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text,
    IExternalProviderRegistry registry, SamlIdentitySource saml, IAuditService audit, Microsoft.Extensions.Options.IOptions<Auth2ServerOptions> options)
    : Auth2PageModel(clientId, flow, urls, text)
{
    public async Task<IActionResult> OnGetAsync(string slug, string name)
    {
        var provider = registry.FindByUrl(slug, name);
        if (provider is null || provider.Type != ExternalProviderType.Saml) { Fail(Auth2ErrorCodes.ExternalProviderDisabled, StatusCodes.Status404NotFound); return Page(); }
        Svc<IClientIdAccessor>().ClientId = provider.ClientId;
        await LoadApplicationAsync();
        var issuer = !string.IsNullOrWhiteSpace(options.Value.Issuer) ? options.Value.Issuer.TrimEnd('/') : $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        try
        {
            var query = Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
            var result = await saml.ReadLogoutRequestAsync(provider, issuer, Request.QueryString.Value ?? "", query, HttpContext.RequestAborted);

            var users = Svc<UserManager<AppUser>>();
            var user = await users.FindByLoginAsync(provider.Name, result.NameId);
            if (user is not null)
            {
                await Svc<ISessionService>().RevokeAllAsync(user, Application!, "saml.single_logout", ct: HttpContext.RequestAborted);
                await audit.WriteAsync(new AuditEntry("saml.slo", Application!.Id, ActorUserId: user.Id, TargetType: "user", TargetId: user.Id.ToString(),
                    Data: new Dictionary<string, object?> { ["provider"] = provider.Name }), HttpContext.RequestAborted);
            }
            var session = await HttpContext.AuthenticateAsync(Auth2Cookie.Scheme);
            if (session.Succeeded && (user is null || session.Principal!.FindFirst("sub")?.Value == user.Id.ToString("D"))) await HttpContext.SignOutAsync(Auth2Cookie.Scheme);
            return Redirect(result.ResponseUrl.ToString());
        }
        catch (ExternalLoginException ex)
        {
            await audit.WriteAsync(new AuditEntry("saml.assertion.rejected", Application?.Id, Data: new Dictionary<string, object?> { ["provider"] = provider.Name, ["code"] = ex.ErrorCode, ["reason"] = ex.Reason }), HttpContext.RequestAborted);
            Fail(ex.ErrorCode);
            return Page();
        }
    }
}
