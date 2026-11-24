using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>
/// SAML 2.0 service provider (SP). Challenge builds an AuthnRequest; the ACS reads and validates the response.
/// Defences, each with a test: XXE-safe parsing, size limit, signature (assertion, and response when required) with pinned certificates,
/// exactly one assertion and unique ids (signature wrapping), issuer, destination, audience, InResponseTo, time window with clock skew,
/// single-use request and assertion ids, and only the validated token of the library is used for claims.
/// </summary>
public sealed partial class SamlIdentitySource(
    ISamlProviderMaterial material, IExternalSecretProtector secrets, IDataProtectionProvider protection, IReplayCache replay,
    IOptions<Auth2ServerOptions> options, TimeProvider clock, ILogger<SamlIdentitySource> log) : IRedirectIdentitySource
{
    private readonly IDataProtector _state = protection.CreateProtector("SystemX.Auth2.SamlState.v1");

    private sealed record StatePayload(string R, string I, string RS, string U, long E, string? L);

    /// <inheritdoc />
    public bool Handles(ExternalProviderType type) => type == ExternalProviderType.Saml;

    private static string Fail(string reason, string code = Auth2ErrorCodes.SamlResponseInvalid) => throw new ExternalLoginException(code, reason);

    /// <summary>The entity id of this SP for a provider: the setting, or <c>{issuer}/saml/{slug}/{name}</c>.</summary>
    public static string SpEntityId(SamlProviderSettings s, RegisteredProvider p, string issuer) =>
        string.IsNullOrWhiteSpace(s.SpEntityId) ? issuer.TrimEnd('/') + $"/saml/{p.ProjectSlug}/{p.Name}" : s.SpEntityId;

    private static string AcsUrl(string issuer, RegisteredProvider p) => issuer.TrimEnd('/') + ExternalProviderUrls.SamlAcsPath(p.ProjectSlug, p.Name);

    private X509Certificate2? SpCertificate(SamlProviderSettings s) =>
        secrets.Unprotect(s.SpSigningCertificateProtected) is { } pfx ? SamlSpCertificates.Load(pfx) : null;

    // ------------------------------------------------------------------------------------ challenge

    /// <inheritdoc />
    public async Task<ExternalChallenge> ChallengeAsync(ExternalChallengeContext ctx, CancellationToken ct)
    {
        var p = ctx.Provider;
        var s = (SamlProviderSettings)p.Settings;
        var idp = await material.GetAsync(p, ct);
        var lifetime = TimeSpan.FromMinutes(Math.Max(1, options.Value.ExternalProviders.StateMinutes));

        var config = new Saml2Configuration { Issuer = SpEntityId(s, p, ctx.Issuer), SingleSignOnDestination = idp.SsoUrl };
        if (s.SignAuthnRequests)
        {
            config.SigningCertificate = SpCertificate(s) ?? throw new ExternalLoginException(Auth2ErrorCodes.SamlResponseInvalid, "no_sp_certificate");
            config.SignAuthnRequest = true;
            config.SignatureAlgorithm = Saml2SecurityAlgorithms.RsaSha256Signature;
        }
        var request = new Saml2AuthnRequest(config)
        {
            AssertionConsumerServiceUrl = new Uri(AcsUrl(ctx.Issuer, p)),
            ForceAuthn = s.ForceAuthn,
            NameIdPolicy = new NameIdPolicy { AllowCreate = true, Format = NameIdFormat(s.NameIdFormat) },
        };
        if (!string.IsNullOrWhiteSpace(s.RequiredAuthnContext))
            request.RequestedAuthnContext = new RequestedAuthnContext { Comparison = AuthnContextComparisonTypes.Exact, AuthnContextClassRef = [s.RequiredAuthnContext] };

        var relay = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var payload = new StatePayload(p.RegistrationName, request.IdAsString, relay, ctx.ReturnUrl, clock.GetUtcNow().Add(lifetime).ToUnixTimeSeconds(), ctx.LinkUserId);
        var sealedState = _state.Protect(JsonSerializer.Serialize(payload));

        if (s.AuthnRequestBinding == SamlBinding.Post)
        {
            var post = new Saml2PostBinding { RelayState = relay };
            post.Bind(request);
            var fields = new Dictionary<string, string>
            {
                ["SAMLRequest"] = Regex.Match(post.PostContent, "name=\"SAMLRequest\" value=\"([^\"]+)\"").Groups[1].Value,
                ["RelayState"] = relay,
            };
            return new ExternalChallenge(idp.SsoUrl, sealedState, lifetime) { PostFields = fields };
        }
        var redirect = new Saml2RedirectBinding { RelayState = relay };
        redirect.Bind(request);
        return new ExternalChallenge(redirect.RedirectLocation, sealedState, lifetime);
    }

    private static string NameIdFormat(SamlNameIdFormat f) => f switch
    {
        SamlNameIdFormat.EmailAddress => "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress",
        SamlNameIdFormat.Persistent => "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent",
        SamlNameIdFormat.Transient => "urn:oasis:names:tc:SAML:2.0:nameid-format:transient",
        _ => "urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified",
    };

    // ------------------------------------------------------------------------------------ ACS

    /// <inheritdoc />
    public async Task<ExternalCallbackResult> AuthenticateCallbackAsync(ExternalCallbackContext ctx, CancellationToken ct)
    {
        var p = ctx.Provider;
        var s = (SamlProviderSettings)p.Settings;
        var now = clock.GetUtcNow();
        var skew = TimeSpan.FromSeconds(Math.Clamp(s.ClockSkewSeconds, 0, 300));

        if (!ctx.Parameters.TryGetValue("SAMLResponse", out var b64) || string.IsNullOrWhiteSpace(b64)) Fail("saml_response_missing");
        if (b64!.Length > SamlXml.MaxBytes * 4 / 3 + 16) Fail("saml_too_large");

        // 1. safe parse and structure checks, before any signature work
        string xml;
        XmlDocument doc;
        try
        {
            xml = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            doc = SamlXml.LoadSafe(xml);
        }
        catch (Exception ex) when (ex is FormatException or XmlException or DecoderFallbackException) { throw new ExternalLoginException(Auth2ErrorCodes.SamlResponseInvalid, "saml_xml_invalid", ex); }
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
        ns.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        var root = doc.DocumentElement;
        if (root is null || root.LocalName != "Response" || root.NamespaceURI != "urn:oasis:names:tc:SAML:2.0:protocol") Fail("saml_not_a_response");

        var plain = root!.SelectNodes("saml:Assertion", ns)!.Count;
        var encrypted = root.SelectNodes("saml:EncryptedAssertion", ns)!.Count;
        if (plain + encrypted != 1) Fail("saml_assertion_count");   // blocks signature wrapping: no second, forged assertion
        if (root.SelectNodes("//*[@ID]")!.Cast<XmlNode>().GroupBy(n => n.Attributes!["ID"]!.Value).Any(g => g.Count() > 1)) Fail("saml_duplicate_id");
        if (s.WantAssertionsEncrypted && encrypted != 1) Fail("saml_assertion_not_encrypted");
        var responseSigned = root.SelectSingleNode("ds:Signature", ns) is not null;
        if (s.WantResponseSigned && !responseSigned) Fail("saml_response_not_signed");
        var responseId = root.GetAttribute("ID");
        var inResponseTo = root.GetAttribute("InResponseTo");

        // 2. state (set at the challenge) or IdP-initiated
        StatePayload? state = null;
        if (!string.IsNullOrEmpty(ctx.ProtectedState))
        {
            try { state = JsonSerializer.Deserialize<StatePayload>(_state.Unprotect(ctx.ProtectedState)); }
            catch (Exception ex) when (ex is CryptographicException or JsonException) { throw new ExternalLoginException(Auth2ErrorCodes.SamlResponseInvalid, "state_invalid", ex); }
        }
        var idpInitiated = false;
        if (state is null || string.IsNullOrEmpty(inResponseTo))
        {
            if (!s.AllowIdpInitiated) Fail(state is null ? "saml_state_missing" : "saml_in_response_to_missing");
            if (!responseSigned) Fail("saml_idp_initiated_needs_signed_response");   // an unsolicited answer must at least be signed as a whole
            idpInitiated = true;
        }
        else
        {
            if (state.E < now.ToUnixTimeSeconds()) Fail("state_expired");
            if (!string.Equals(state.R, p.RegistrationName, StringComparison.Ordinal)) Fail("wrong_provider");
            if (!string.Equals(state.I, inResponseTo, StringComparison.Ordinal)) Fail("in_response_to_mismatch");
            if (ctx.Parameters.TryGetValue("RelayState", out var rs) && !string.Equals(rs, state.RS, StringComparison.Ordinal)) Fail("relay_state_mismatch");
            if (!await replay.TryAddAsync(p.ApplicationId, $"saml-request:{p.RegistrationName}:{state.I}", DateTimeOffset.FromUnixTimeSeconds(state.E).Add(skew), ct))
                Fail("saml_request_replayed", Auth2ErrorCodes.SamlReplayDetected);
        }

        // 3. signature and claims through the library, with pinned certificates
        var idp = await material.GetAsync(p, ct);
        var entityId = SpEntityId(s, p, ctx.Issuer);
        var config = new Saml2Configuration
        {
            Issuer = entityId,
            AllowedIssuer = idp.EntityId,
            AudienceRestricted = true,
            CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None,   // the certificates are pinned in the settings
            RevocationMode = X509RevocationMode.NoCheck,
            SaveBootstrapContext = false,
            DetectReplayedTokens = false,   // we keep our own shared cache below
        };
        config.AllowedAudienceUris.Add(entityId);
        foreach (var c in idp.SigningCertificates) config.SignatureValidationCertificates.Add(c);
        if (SpCertificate(s) is { } own) config.DecryptionCertificates.Add(own);

        Saml2AuthnResponse response;
        try
        {
            var request = new ITfoxtec.Identity.Saml2.Http.HttpRequest { Method = "POST", Form = new System.Collections.Specialized.NameValueCollection { ["SAMLResponse"] = b64 } };
            response = new Saml2AuthnResponse(config);
            var binding = new Saml2PostBinding();
            binding.ReadSamlResponse(request, response);
            if (response.Status != Saml2StatusCodes.Success) Fail("saml_status_" + response.Status.ToString().ToLowerInvariant());
            binding.Unbind(request, response);
        }
        catch (ExternalLoginException) { throw; }
        catch (Exception ex)
        {
            log.LogWarning("A SAML response for {Provider} was rejected: {Kind}.", p.RegistrationName, ex.GetType().Name);
            throw new ExternalLoginException(Auth2ErrorCodes.SamlResponseInvalid, "saml_validation_failed", ex);
        }

        // 4. our own checks on top of the library
        if (!string.Equals(response.Issuer, idp.EntityId, StringComparison.Ordinal)) Fail("issuer_mismatch");
        if (response.Destination is null || !string.Equals(response.Destination.AbsoluteUri, new Uri(AcsUrl(ctx.Issuer, p)).AbsoluteUri, StringComparison.Ordinal)) Fail("destination_mismatch");
        var token = response.Saml2SecurityToken ?? throw new ExternalLoginException(Auth2ErrorCodes.SamlResponseInvalid, "no_token");
        var assertion = token.Assertion;
        if (s.WantAssertionsSigned && assertion.Signature is null) Fail("assertion_not_signed");
        if (plain == 1 && !string.Equals(root.SelectSingleNode("saml:Assertion", ns)!.Attributes!["ID"]!.Value, token.Id, StringComparison.Ordinal)) Fail("assertion_id_mismatch");

        var conditions = assertion.Conditions;
        var nowUtc = now.UtcDateTime;
        if (conditions is null) Fail("assertion_no_conditions");
        if (conditions!.NotBefore is { } nb && nb > nowUtc + skew) Fail("assertion_not_yet_valid");
        if (conditions.NotOnOrAfter is { } na && na <= nowUtc - skew) Fail("assertion_expired");
        foreach (var confirmation in assertion.Subject?.SubjectConfirmations ?? [])
        {
            var data = confirmation.SubjectConfirmationData;
            if (data?.NotOnOrAfter is { } cna && cna <= nowUtc - skew) Fail("subject_confirmation_expired");
            if (data?.Recipient is { } rec && !string.Equals(rec.AbsoluteUri, new Uri(AcsUrl(ctx.Issuer, p)).AbsoluteUri, StringComparison.Ordinal)) Fail("recipient_mismatch");
            if (!idpInitiated && data?.InResponseTo is { } sir && !string.Equals(sir.Value, state!.I, StringComparison.Ordinal)) Fail("in_response_to_mismatch");
        }
        var authnContext = assertion.Statements.OfType<Microsoft.IdentityModel.Tokens.Saml2.Saml2AuthenticationStatement>().FirstOrDefault()?.AuthenticationContext?.ClassReference?.OriginalString;
        if (!string.IsNullOrWhiteSpace(s.RequiredAuthnContext) && !string.Equals(authnContext, s.RequiredAuthnContext, StringComparison.Ordinal)) Fail("authn_context_mismatch");

        // 5. the assertion id is single use until it can no longer be valid
        var keep = (conditions.NotOnOrAfter ?? nowUtc.AddMinutes(10)).Add(skew);
        if (!await replay.TryAddAsync(p.ApplicationId, $"saml-assertion:{p.RegistrationName}:{token.Id}", new DateTimeOffset(DateTime.SpecifyKind(keep, DateTimeKind.Utc)), ct))
            Fail("saml_assertion_replayed", Auth2ErrorCodes.SamlReplayDetected);

        var identity = BuildIdentity(p, s, idp, response, authnContext);
        var returnUrl = idpInitiated ? "" : state!.U;   // RelayState is never trusted for redirects
        return new ExternalCallbackResult(identity, returnUrl, idpInitiated ? null : state!.L) { IdpInitiated = idpInitiated };
    }

    // ------------------------------------------------------------------------------------ single logout (IdP-initiated)

    /// <summary>Result of reading a logout request from the identity provider.</summary>
    public sealed record LogoutRequestResult(string NameId, string? SessionIndex, Uri ResponseUrl);

    /// <summary>
    /// Reads a LogoutRequest sent by the identity provider (HTTP-Redirect binding). The query must be signed with a pinned IdP certificate.
    /// Returns who to sign out and where to send the LogoutResponse.
    /// </summary>
    public async Task<LogoutRequestResult> ReadLogoutRequestAsync(RegisteredProvider p, string issuer, string rawQuery, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var s = (SamlProviderSettings)p.Settings;
        if (!s.AllowSingleLogout) Fail("slo_not_allowed");
        if (!query.ContainsKey("Signature") || !query.ContainsKey("SigAlg")) Fail("slo_not_signed");
        var idp = await material.GetAsync(p, ct);
        if (idp.SloUrl is null) Fail("slo_no_idp_endpoint");
        var config = new Saml2Configuration { Issuer = SpEntityId(s, p, issuer), AllowedIssuer = idp.EntityId, CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None, RevocationMode = X509RevocationMode.NoCheck };
        foreach (var c in idp.SigningCertificates) config.SignatureValidationCertificates.Add(c);
        try
        {
            var request = new ITfoxtec.Identity.Saml2.Http.HttpRequest { Method = "GET", QueryString = rawQuery, Query = [] };
            foreach (var (k, v) in query) request.Query.Add(k, v);
            var logout = new Saml2LogoutRequest(config);
            var binding = new Saml2RedirectBinding();
            binding.ReadSamlRequest(request, logout);
            binding.Unbind(request, logout);
            if (!string.Equals(logout.Issuer, idp.EntityId, StringComparison.Ordinal)) Fail("issuer_mismatch");
            if (logout.NotOnOrAfter is { } limit && limit.UtcDateTime < clock.GetUtcNow().UtcDateTime - TimeSpan.FromSeconds(Math.Clamp(s.ClockSkewSeconds, 0, 300))) Fail("slo_expired");
            if (!await replay.TryAddAsync(p.ApplicationId, $"saml-logout:{p.RegistrationName}:{logout.IdAsString}", clock.GetUtcNow().AddMinutes(10), ct)) Fail("saml_request_replayed", Auth2ErrorCodes.SamlReplayDetected);

            var response = new Saml2LogoutResponse(config) { Status = Saml2StatusCodes.Success, InResponseTo = logout.Id, Destination = idp.SloUrl };
            var location = idp.SloUrl!;
            if (SpCertificate(s) is { } cert)
            {
                config.SigningCertificate = cert;
                var outBinding = new Saml2RedirectBinding();
                outBinding.Bind(response);
                location = outBinding.RedirectLocation;
            }
            return new LogoutRequestResult(logout.NameId?.Value ?? "", logout.SessionIndex, location);
        }
        catch (ExternalLoginException) { throw; }
        catch (Exception ex) { throw new ExternalLoginException(Auth2ErrorCodes.SamlResponseInvalid, "slo_invalid", ex); }
    }

    private static ExternalIdentity BuildIdentity(RegisteredProvider p, SamlProviderSettings s, SamlIdpMaterial idp, Saml2AuthnResponse response, string? authnContext)
    {
        var attrs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in response.ClaimsIdentity.Claims.GroupBy(c => c.Type)) attrs[group.Key] = [.. group.Select(c => c.Value)];

        string? First(params string?[] names)
        {
            foreach (var n in names.Where(n => !string.IsNullOrEmpty(n)))
                if (attrs.TryGetValue(n!, out var v) && v.Length > 0 && v[0].Length > 0) return v[0];
            return null;
        }
        string? Mapped(string local) => s.AttributeMappings.Where(kv => kv.Value == local).Select(kv => First(kv.Key)).FirstOrDefault(v => v is not null);

        var subject = s.SubjectAttribute is { Length: > 0 } sa ? First(sa) : response.NameId?.Value;
        var email = Mapped("email") ?? First(s.EmailAttribute, "email", "mail", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
            ?? (response.NameId?.Format?.OriginalString?.EndsWith("emailAddress", StringComparison.Ordinal) == true ? response.NameId.Value : null);
        var groups = s.GroupsAttribute is { Length: > 0 } g && attrs.TryGetValue(g, out var gv) ? [.. gv] : new List<string>();
        return new ExternalIdentity(p.Name, subject ?? "", idp.EntityId, email, email is null ? null : true,
            Mapped("given_name") ?? First("givenname", "given_name", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname"),
            Mapped("family_name") ?? First("surname", "family_name", "sn", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname"),
            Mapped("phone_number") ?? First("phone", "telephoneNumber", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/mobilephone"), groups, attrs)
        {
            DisplayName = Mapped("name") ?? First("displayname", "name", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"),
            UsedMultiFactor = authnContext is null ? null : authnContext.Contains("MultiFactor", StringComparison.OrdinalIgnoreCase) ? true : null,
        };
    }
}
