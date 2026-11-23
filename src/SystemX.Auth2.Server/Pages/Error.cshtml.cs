using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SystemX.Auth2.Server.Pages;

/// <summary>Generic error page. It only shows the request id, never details.</summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    /// <summary>Id to give to support.</summary>
    public string? RequestId { get; private set; }

    /// <summary>Loads the request id.</summary>
    public void OnGet() => RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
