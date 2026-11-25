#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages;

/// <summary>The terms of use of this server. Public.</summary>
public sealed class TermsModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : LegalPageModel(clientId, flow, urls, text);
