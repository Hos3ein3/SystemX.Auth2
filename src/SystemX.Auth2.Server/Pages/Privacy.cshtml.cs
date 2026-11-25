#pragma warning disable CS1591 // Page models are not a public API; the UI reads their properties (see docs/ui-handover.md).
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages;

/// <summary>The privacy page: what this server stores about a person, why, and what the person can do about it. Public.</summary>
public sealed class PrivacyModel(IClientIdAccessor clientId, IAuthFlowResolver flow, IReturnUrlValidator urls, IAuth2Localizer text)
    : LegalPageModel(clientId, flow, urls, text);
