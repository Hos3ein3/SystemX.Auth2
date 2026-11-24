#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Infrastructure.External;

namespace SystemX.Auth2.Server.Pages.Saml;

/// <summary>The metadata of this Server as a SAML service provider. Public. It has no secrets: the public certificate, the entity id and the URLs.</summary>
public sealed class MetadataModel(IExternalProviderRegistry registry, SamlMetadataBuilder builder, IOptions<Auth2ServerOptions> options) : PageModel
{
    public IActionResult OnGet(string slug, string name)
    {
        var provider = registry.FindByUrl(slug, name);
        if (provider is null || provider.Type != ExternalProviderType.Saml) return NotFound();
        var issuer = !string.IsNullOrWhiteSpace(options.Value.Issuer) ? options.Value.Issuer.TrimEnd('/') : $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        return Content(builder.Build(provider, issuer), "application/samlmetadata+xml");
    }
}
