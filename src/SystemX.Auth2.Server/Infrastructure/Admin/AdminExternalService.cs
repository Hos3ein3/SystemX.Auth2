using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Entities;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.Auth;
using SystemX.Auth2.Server.Infrastructure.External;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Admin;

/// <summary>See <see cref="IAdminExternalService"/>. Secrets are encrypted before they are saved and are never put into an answer or an audit event.</summary>
internal sealed class AdminExternalService(
    Auth2DbContext db, IGlobalScope globalScope, IApplicationService applications, IExternalSecretProtector secrets, IExternalProviderDescriptors descriptors,
    IOidcEndpointResolver oidc, ISamlProviderMaterial saml, ILdapDirectory ldap, UserManager<AppUser> users,
    IExternalLinkService links, UserLookup lookup, IAuthFlowResolver flow, IServerValues serverValues, IAuditService audit, IOptions<Auth2ServerOptions> options, TimeProvider clock) : IAdminExternalService
{
    private static readonly string[] BlockedKey = ["passkeys.blocked_aaguids"];

    // ------------------------------------------------------------------------------------ helpers

    private bool Single => options.Value.Mode == ServerMode.SingleApplication;

    private async Task<Auth2Application?> AppAsync(Guid id, CancellationToken ct)
    {
        using var _ = globalScope.Begin();
        return await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    private string Issuer() => !string.IsNullOrWhiteSpace(options.Value.Issuer) ? options.Value.Issuer.TrimEnd('/') : "https://auth.example.com";

    private ExternalProviderAdminDto ToDto(Auth2Application app, ExternalProviderSettings p)
    {
        var slug = Single ? ExternalProviderUrls.SingleModeSlug : app.ProjectKey;
        var urls = ExternalProviderUrls.For(new RegisteredProvider(app.Id, slug, p, app.SettingsVersion, app.ClientId), Issuer());
        var set = new Dictionary<string, bool>();
        var warnings = new List<string>();
        switch (p)
        {
            case OidcProviderSettings o: set["clientSecret"] = !string.IsNullOrEmpty(o.ClientSecretProtected); set["privateKey"] = !string.IsNullOrEmpty(o.PrivateKeyProtected); break;
            case SamlProviderSettings s:
                set["spCertificate"] = !string.IsNullOrEmpty(s.SpSigningCertificateProtected);
                foreach (var c in s.IdpSigningCertificates)
                    try { using var cert = SamlXml.LoadCertificate(c); if (cert.NotAfter.ToUniversalTime() < clock.GetUtcNow().UtcDateTime.AddDays(30)) warnings.Add("An identity provider certificate expires within 30 days."); }
                    catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException) { warnings.Add("An identity provider certificate cannot be read."); }
                if (s.AllowIdpInitiated) warnings.Add("IdP-initiated login is on.");
                break;
            case LdapProviderSettings l:
                set["bindPassword"] = !string.IsNullOrEmpty(l.BindPasswordProtected);
                if (!l.ValidateServerCertificate) warnings.Add("The server certificate is not checked.");
                break;
        }
        return new ExternalProviderAdminDto(p.Name, p.Type, p.Enabled, p.DisplayName, SettingsJson.ProviderToElement(secrets.Redact(p)), set, urls, ExternalProviderUrls.RegistrationName(app.Id, p.Name), warnings);
    }

    /// <summary>Fields that only a global admin may switch to a weaker or more trusting value.</summary>
    private static IEnumerable<string> RestrictedChanges(ExternalProviderSettings? before, ExternalProviderSettings after)
    {
        bool Was(Func<ExternalProviderSettings, bool?> f) => before is not null && f(before) == true;
        if (after.TrustProviderEmailVerification == true && !Was(p => p.TrustProviderEmailVerification)) yield return "TrustProviderEmailVerification";
        if (after.TrustProviderMfa && !Was(p => p.TrustProviderMfa)) yield return "TrustProviderMfa";
        if (after is SamlProviderSettings { AllowIdpInitiated: true } && !Was(p => (p as SamlProviderSettings)?.AllowIdpInitiated)) yield return "AllowIdpInitiated";
        if (after is LdapProviderSettings { ValidateServerCertificate: false } && (before is not LdapProviderSettings { ValidateServerCertificate: false })) yield return "ValidateServerCertificate";
    }

    private static (string? ClientSecret, string? PrivateKey, string? BindPassword) PlainSecrets(JsonElement root)
    {
        string? Get(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
        return (Get("clientSecret"), Get("privateKey"), Get("bindPassword"));
    }

    private ExternalProviderSettings? Parse(string body, out (string? ClientSecret, string? PrivateKey, string? BindPassword) plain, out OperationResult? error)
    {
        plain = default; error = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            plain = PlainSecrets(doc.RootElement);
            var parsed = SettingsJson.DeserializeProvider(body);
            return parsed;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            error = OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
            return null;
        }
    }

    private void ApplySecrets(ExternalProviderSettings p, ExternalProviderSettings? old, (string? ClientSecret, string? PrivateKey, string? BindPassword) plain)
    {
        // The Protected fields of a request are never trusted: they come from the old value or from the plain secret.
        switch (p)
        {
            case OidcProviderSettings o:
                var oo = old as OidcProviderSettings;
                o.ClientSecretProtected = plain.ClientSecret is not null ? secrets.Protect(plain.ClientSecret) : oo?.ClientSecretProtected;
                o.PrivateKeyProtected = plain.PrivateKey is not null ? secrets.Protect(plain.PrivateKey) : oo?.PrivateKeyProtected;
                break;
            case SamlProviderSettings s:
                s.SpSigningCertificateProtected = (old as SamlProviderSettings)?.SpSigningCertificateProtected;
                break;
            case LdapProviderSettings l:
                l.BindPasswordProtected = plain.BindPassword is not null ? secrets.Protect(plain.BindPassword) : (old as LdapProviderSettings)?.BindPasswordProtected;
                break;
        }
    }

    private async Task<OperationResult> SaveAsync(Guid appId, ApplicationSettings settings, CancellationToken ct)
    {
        var errors = ApplicationSettingsValidator.Validate(settings);
        if (errors.Count > 0) return AdminSupport.Invalid(errors);
        await applications.UpdateSettingsAsync(appId, settings, ct);   // bumps the version, refreshes the registry on this instance, tells the others
        return OperationResult.Ok;
    }

    // ------------------------------------------------------------------------------------ providers

    public IReadOnlyList<ExternalProviderTypeDto> Types() =>
    [.. descriptors.All.OrderBy(d => d.Type).Select(d => new ExternalProviderTypeDto(d.Type, d.DisplayName,
        d.Type == ExternalProviderType.Saml ? "saml" : d.Type == ExternalProviderType.Ldap ? "ldap" : "oidc",
        [.. d.Fields.Select(f => new ProviderFieldDto(f.Name, f.LabelKey, f.Kind.ToString(), f.Required, f.Default, f.HelpKey))], d.DefaultScopes, d.SetupDocsAnchor, d.EmailVerifiedByDefault))];

    public async Task<IReadOnlyList<ExternalProviderAdminDto>?> ListProvidersAsync(Guid applicationId, CancellationToken ct = default)
    {
        var app = await AppAsync(applicationId, ct);
        return app is null ? null : [.. app.Settings.ExternalProviders.Select(p => ToDto(app, p))];
    }

    public async Task<(OperationResult, ExternalProviderAdminDto?)> CreateProviderAsync(Guid applicationId, string body, bool isGlobalAdmin, CancellationToken ct = default)
    {
        var app = await AppAsync(applicationId, ct);
        if (app is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        var provider = Parse(body, out var plain, out var error);
        if (provider is null) return (error!, null);
        if (!isGlobalAdmin && RestrictedChanges(null, provider).ToList() is { Count: > 0 } restricted)
            return (AdminSupport.Invalid(restricted.Select(r => new SettingsError(r, "Only a global admin may turn this on."))) with { ErrorCode = Auth2ErrorCodes.Forbidden }, null);
        if (app.Settings.ExternalProviders.Any(x => x.Name == provider.Name)) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);

        ApplySecrets(provider, null, plain);
        var settings = SettingsJson.Clone(app.Settings);
        settings.ExternalProviders.Add(provider);
        var saved = await SaveAsync(applicationId, settings, ct);
        if (!saved.Succeeded) return (saved, null);
        await audit.WriteAsync(new AuditEntry("external.provider.created", applicationId, TargetType: "external_provider", TargetId: provider.Name,
            Data: new Dictionary<string, object?> { ["type"] = provider.Type.ToString(), ["enabled"] = provider.Enabled }), ct);
        return (OperationResult.Ok, ToDto((await AppAsync(applicationId, ct))!, provider));
    }

    public async Task<(OperationResult, ExternalProviderAdminDto?)> UpdateProviderAsync(Guid applicationId, string name, string body, bool isGlobalAdmin, int? ifMatchVersion, CancellationToken ct = default)
    {
        var app = await AppAsync(applicationId, ct);
        var old = app?.Settings.ExternalProviders.FirstOrDefault(x => x.Name == name);
        if (app is null || old is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        if (ifMatchVersion is null) return (OperationResult.Fail(Auth2ErrorCodes.ValidationFailed), null);   // the row version is required
        if (ifMatchVersion != app.SettingsVersion) return (OperationResult.Fail(Auth2ErrorCodes.Conflict), null);

        var provider = Parse(body, out var plain, out var error);
        if (provider is null) return (error!, null);
        if (!string.IsNullOrEmpty(provider.Name) && provider.Name != name) return (AdminSupport.Invalid([new SettingsError("Name", "A provider cannot be renamed: its users are linked by the name.")]), null);
        provider.Name = name;
        if (provider.Type != old.Type) return (AdminSupport.Invalid([new SettingsError("Type", "The type cannot be changed.")]), null);
        if (!isGlobalAdmin && RestrictedChanges(old, provider).ToList() is { Count: > 0 } restricted)
            return (AdminSupport.Invalid(restricted.Select(r => new SettingsError(r, "Only a global admin may turn this on."))) with { ErrorCode = Auth2ErrorCodes.Forbidden }, null);

        ApplySecrets(provider, old, plain);
        var settings = SettingsJson.Clone(app.Settings);
        settings.ExternalProviders[settings.ExternalProviders.FindIndex(x => x.Name == name)] = provider;
        var saved = await SaveAsync(applicationId, settings, ct);
        if (!saved.Succeeded) return (saved, null);
        await audit.WriteAsync(new AuditEntry("external.provider.updated", applicationId, TargetType: "external_provider", TargetId: name,
            Data: new Dictionary<string, object?> { ["type"] = provider.Type.ToString(), ["enabled"] = provider.Enabled }), ct);
        return (OperationResult.Ok, ToDto((await AppAsync(applicationId, ct))!, provider));
    }

    public async Task<OperationResult> RotateSecretAsync(Guid applicationId, string name, RotateProviderSecretRequest request, CancellationToken ct = default)
    {
        var app = await AppAsync(applicationId, ct);
        var current = app?.Settings.ExternalProviders.FirstOrDefault(x => x.Name == name);
        if (app is null || current is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var settings = SettingsJson.Clone(app.Settings);
        var p = settings.ExternalProviders.First(x => x.Name == name);
        switch (p)
        {
            case OidcProviderSettings o when p.Type == ExternalProviderType.Apple:
                if (string.IsNullOrEmpty(request.PrivateKey)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
                o.PrivateKeyProtected = secrets.Protect(request.PrivateKey); break;
            case OidcProviderSettings o:
                if (string.IsNullOrEmpty(request.ClientSecret)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
                o.ClientSecretProtected = secrets.Protect(request.ClientSecret); break;
            case LdapProviderSettings l:
                if (string.IsNullOrEmpty(request.BindPassword)) return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
                l.BindPasswordProtected = secrets.Protect(request.BindPassword); break;
            default: return OperationResult.Fail(Auth2ErrorCodes.ValidationFailed);
        }
        var saved = await SaveAsync(applicationId, settings, ct);
        if (saved.Succeeded) await audit.WriteAsync(new AuditEntry("external.provider.secret_rotated", applicationId, TargetType: "external_provider", TargetId: name), ct);
        return saved;
    }

    public async Task<OperationResult> DeleteProviderAsync(Guid applicationId, string name, CancellationToken ct = default)
    {
        var app = await AppAsync(applicationId, ct);
        if (app is null || app.Settings.ExternalProviders.All(x => x.Name != name)) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var settings = SettingsJson.Clone(app.Settings);
        settings.ExternalProviders.RemoveAll(x => x.Name == name);
        var saved = await SaveAsync(applicationId, settings, ct);
        if (saved.Succeeded) await audit.WriteAsync(new AuditEntry("external.provider.deleted", applicationId, TargetType: "external_provider", TargetId: name), ct);
        return saved;
    }

    // ------------------------------------------------------------------------------------ tests

    private (Auth2Application? App, RegisteredProvider? Provider) Find(Auth2Application? app, string name)
    {
        var p = app?.Settings.ExternalProviders.FirstOrDefault(x => x.Name == name);
        return p is null ? (null, null) : (app, new RegisteredProvider(app!.Id, Single ? ExternalProviderUrls.SingleModeSlug : app.ProjectKey, p, app.SettingsVersion, app.ClientId));
    }

    public async Task<(OperationResult, ExternalProviderTestResult?)> TestProviderAsync(Guid applicationId, string name, CancellationToken ct = default)
    {
        var (app, provider) = Find(await AppAsync(applicationId, ct), name);
        if (provider is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        var details = new Dictionary<string, string>();
        try
        {
            switch (provider.Settings)
            {
                case OidcProviderSettings:
                    var ep = await oidc.ResolveAsync(provider, ct, refresh: true);
                    details["issuer"] = ep.Issuer; details["authorizationEndpoint"] = ep.AuthorizationEndpoint.ToString(); details["tokenEndpoint"] = ep.TokenEndpoint.ToString();
                    if (ep.UserInfoEndpoint is not null) details["userInfoEndpoint"] = ep.UserInfoEndpoint.ToString();
                    details["signingKeys"] = ep.SigningKeys.Count.ToString(); details["flow"] = "authorization code with PKCE (S256)";
                    break;
                case SamlProviderSettings:
                    var m = await saml.RefreshAsync(provider, ct);
                    details["entityId"] = m.EntityId; details["ssoUrl"] = m.SsoUrl.ToString(); if (m.SloUrl is not null) details["sloUrl"] = m.SloUrl.ToString();
                    var i = 0;
                    foreach (var c in m.SigningCertificates) details[$"certificate{++i}"] = $"expires {c.NotAfter.ToUniversalTime():yyyy-MM-dd} ({(int)(c.NotAfter.ToUniversalTime() - clock.GetUtcNow().UtcDateTime).TotalDays} days)";
                    break;
                case LdapProviderSettings l:
                    await ldap.TestConnectionAsync(provider, ct);
                    details["host"] = $"{l.Host}:{l.Port}"; details["security"] = l.Security.ToString(); details["serviceAccountBind"] = "ok";
                    break;
            }
            return (OperationResult.Ok, new ExternalProviderTestResult(true, null, null, details));
        }
        catch (ExternalLoginException ex)
        {
            return (OperationResult.Ok, new ExternalProviderTestResult(false, ex.ErrorCode, ex.Reason, details));
        }
    }

    public async Task<(OperationResult, LdapTestUserResult?)> LdapTestUserAsync(Guid applicationId, string name, string userName, CancellationToken ct = default)
    {
        var (_, provider) = Find(await AppAsync(applicationId, ct), name);
        if (provider is not { Settings: LdapProviderSettings }) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        try
        {
            var r = await ldap.LookupAsync(provider, userName, ct);
            await audit.WriteAsync(new AuditEntry("ldap.test_user", applicationId, TargetType: "external_provider", TargetId: name), ct);   // not the user name
            return (OperationResult.Ok, new LdapTestUserResult(r.Dn, r.Subject, r.Attributes, r.Groups));
        }
        catch (LdapAuthException ex) { return (OperationResult.Fail(ex.ErrorCode == Auth2ErrorCodes.InvalidCredentials ? Auth2ErrorCodes.NotFound : ex.ErrorCode), null); }
    }

    public async Task<(OperationResult, ExternalProviderTestResult?)> SamlRefreshMetadataAsync(Guid applicationId, string name, CancellationToken ct = default)
    {
        var (_, provider) = Find(await AppAsync(applicationId, ct), name);
        if (provider is not { Settings: SamlProviderSettings }) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        var (_, test) = await TestProviderAsync(applicationId, name, ct);
        await audit.WriteAsync(new AuditEntry("saml.metadata_refreshed", applicationId, TargetType: "external_provider", TargetId: name), ct);
        return (OperationResult.Ok, test);
    }

    public async Task<(OperationResult, SamlSpCertificateDto?)> SamlGenerateCertificateAsync(Guid applicationId, string name, CancellationToken ct = default)
    {
        var app = await AppAsync(applicationId, ct);
        if (app?.Settings.ExternalProviders.FirstOrDefault(x => x.Name == name) is not SamlProviderSettings) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        var (pfx, notAfter) = SamlSpCertificates.Generate($"{app.ProjectKey}-{name}-sp", clock.GetUtcNow());
        var settings = SettingsJson.Clone(app.Settings);
        ((SamlProviderSettings)settings.ExternalProviders.First(x => x.Name == name)).SpSigningCertificateProtected = secrets.Protect(pfx);
        var saved = await SaveAsync(applicationId, settings, ct);
        if (!saved.Succeeded) return (saved, null);
        await audit.WriteAsync(new AuditEntry("saml.sp_certificate_generated", applicationId, TargetType: "external_provider", TargetId: name), ct);
        using var cert = SamlSpCertificates.Load(pfx);
        return (OperationResult.Ok, new SamlSpCertificateDto(cert.ExportCertificatePem(), notAfter, cert.Thumbprint));
    }

    // ------------------------------------------------------------------------------------ mappings

    private static ExternalMappingDto ToDto(ExternalGroupMapping m) =>
        new(m.Id, m.ProviderName, m.SourceType.ToString(), m.SourceName, m.MatchType.ToString(), m.SourceValue, m.TargetType.ToString(), m.TargetId);

    public async Task<IReadOnlyList<ExternalMappingDto>> ListMappingsAsync(Guid applicationId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        return [.. (await db.ExternalGroupMappings.AsNoTracking().Where(m => m.ApplicationId == applicationId).OrderBy(m => m.ProviderName).ThenBy(m => m.SourceValue).ToListAsync(ct)).Select(ToDto)];
    }

    private async Task<(OperationResult Result, ExternalGroupMapping? Mapping)> BuildMappingAsync(Guid applicationId, ExternalMappingRequest r, ExternalGroupMapping? existing, CancellationToken ct)
    {
        if (!Enum.TryParse<MappingSourceType>(r.SourceType, true, out var source) || !Enum.TryParse<MappingMatchType>(r.MatchType, true, out var match) || !Enum.TryParse<MappingTargetType>(r.TargetType, true, out var target))
            return (AdminSupport.Invalid([new SettingsError("Type", "Unknown source, match or target type. There are no regular expressions: use Exact or Prefix.")]), null);
        if (string.IsNullOrWhiteSpace(r.SourceValue) || r.SourceValue.Length > 500 || (source != MappingSourceType.Group && string.IsNullOrWhiteSpace(r.SourceName)))
            return (AdminSupport.Invalid([new SettingsError("SourceValue", "A value is needed (and a name for claims and attributes).")]), null);
        var app = await AppAsync(applicationId, ct);
        if (app is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        if (!string.IsNullOrWhiteSpace(r.ProviderName) && app.Settings.ExternalProviders.All(p => p.Name != r.ProviderName))
            return (AdminSupport.Invalid([new SettingsError("ProviderName", "This application has no provider with this name.")]), null);

        using var _ = globalScope.Begin();
        // The target must belong to this application. Global and system roles (GlobalAdmin) can never be a target.
        var ok = target switch
        {
            MappingTargetType.Role => await db.Roles.AnyAsync(x => x.Id == r.TargetId && x.ApplicationId == applicationId && !x.IsSystem && x.Name != Auth2Roles.GlobalAdmin, ct),
            MappingTargetType.Group => await db.Groups.AnyAsync(x => x.Id == r.TargetId && x.ApplicationId == applicationId, ct),
            _ => await db.Permissions.AnyAsync(x => x.Id == r.TargetId && x.ApplicationId == applicationId, ct),
        };
        if (!ok) return (AdminSupport.Invalid([new SettingsError("TargetId", "The target must be a role, group or permission of this application. System roles and GlobalAdmin cannot be mapped.")]), null);

        var m = existing ?? new ExternalGroupMapping { ApplicationId = applicationId };
        (m.ProviderName, m.SourceType, m.SourceName, m.MatchType, m.SourceValue, m.TargetType, m.TargetId) =
            (string.IsNullOrWhiteSpace(r.ProviderName) ? null : r.ProviderName, source, source == MappingSourceType.Group ? "" : r.SourceName.Trim(), match, r.SourceValue.Trim(), target, r.TargetId);
        return (OperationResult.Ok, m);
    }

    public async Task<(OperationResult, ExternalMappingDto?)> CreateMappingAsync(Guid applicationId, ExternalMappingRequest request, CancellationToken ct = default)
    {
        var (result, mapping) = await BuildMappingAsync(applicationId, request, null, ct);
        if (!result.Succeeded) return (result, null);
        using var _ = globalScope.Begin();
        if (await db.ExternalGroupMappings.CountAsync(x => x.ApplicationId == applicationId, ct) >= 1000) return (OperationResult.Fail(Auth2ErrorCodes.TooManyItems), null);
        db.ExternalGroupMappings.Add(mapping!);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("external.mapping.created", applicationId, TargetType: "external_mapping", TargetId: mapping!.Id.ToString()), ct);
        return (OperationResult.Ok, ToDto(mapping));
    }

    public async Task<(OperationResult, ExternalMappingDto?)> UpdateMappingAsync(Guid applicationId, Guid id, ExternalMappingRequest request, CancellationToken ct = default)
    {
        ExternalGroupMapping? existing;
        using (globalScope.Begin()) existing = await db.ExternalGroupMappings.FirstOrDefaultAsync(x => x.Id == id && x.ApplicationId == applicationId, ct);
        if (existing is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), null);
        var (result, mapping) = await BuildMappingAsync(applicationId, request, existing, ct);
        if (!result.Succeeded) return (result, null);
        using var _ = globalScope.Begin();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEntry("external.mapping.updated", applicationId, TargetType: "external_mapping", TargetId: id.ToString()), ct);
        return (OperationResult.Ok, ToDto(mapping!));
    }

    public async Task<OperationResult> DeleteMappingAsync(Guid applicationId, Guid id, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        var n = await db.ExternalGroupMappings.Where(x => x.Id == id && x.ApplicationId == applicationId).ExecuteDeleteAsync(ct);
        if (n == 0) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        await audit.WriteAsync(new AuditEntry("external.mapping.deleted", applicationId, TargetType: "external_mapping", TargetId: id.ToString()), ct);
        return OperationResult.Ok;
    }

    // ------------------------------------------------------------------------------------ email domain routes

    public async Task<IReadOnlyList<EmailDomainRouteDto>?> ListEmailDomainRoutesAsync(Guid applicationId, CancellationToken ct = default)
    {
        var app = await AppAsync(applicationId, ct);
        return app is null ? null : [.. app.Settings.ExternalProviders.Select(p => new EmailDomainRouteDto(p.Name, p.EmailDomains))];
    }

    public async Task<OperationResult> SetEmailDomainRoutesAsync(Guid applicationId, IReadOnlyList<EmailDomainRouteDto> routes, CancellationToken ct = default)
    {
        var app = await AppAsync(applicationId, ct);
        if (app is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var settings = SettingsJson.Clone(app.Settings);
        foreach (var p in settings.ExternalProviders) p.EmailDomains = [];
        foreach (var r in routes)
        {
            var p = settings.ExternalProviders.FirstOrDefault(x => x.Name == r.ProviderName);
            if (p is null) return AdminSupport.Invalid([new SettingsError("ProviderName", $"Unknown provider '{r.ProviderName}'.")]);
            p.EmailDomains = [.. r.Domains.Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).Distinct()];
        }
        var saved = await SaveAsync(applicationId, settings, ct);   // the validator refuses a domain that belongs to two providers
        if (saved.Succeeded) await audit.WriteAsync(new AuditEntry("external.email_domains.updated", applicationId, TargetType: "application", TargetId: applicationId.ToString()), ct);
        return saved;
    }

    // ------------------------------------------------------------------------------------ users

    /// <summary>The user, only when they belong to the application the admin call works on (the other applications do not exist for this call).</summary>
    private async Task<AppUser?> MemberAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        var app = await flow.GetApplicationAsync(ct);
        return await lookup.IsMemberAsync(user, app, ct) ? user : null;
    }

    public async Task<(OperationResult, IReadOnlyList<UserExternalLoginDto>)> ListUserLoginsAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await MemberAsync(userId, ct);
        if (user is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), []);
        return (OperationResult.Ok, [.. (await users.GetLoginsAsync(user)).Select(l => new UserExternalLoginDto(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName))]);
    }

    public async Task<OperationResult> RemoveUserLoginAsync(Guid userId, string provider, bool force, CancellationToken ct = default)
    {
        var user = await MemberAsync(userId, ct);
        var login = user is null ? null : (await users.GetLoginsAsync(user)).FirstOrDefault(l => l.LoginProvider == provider);
        if (user is null || login is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        if (!force) return await links.UnlinkAsync(user, provider, login.ProviderKey, ct);
        var result = await users.RemoveLoginAsync(user, provider, login.ProviderKey);
        if (result.Succeeded) await audit.WriteAsync(new AuditEntry("external.login.unlinked", null, TargetType: "user", TargetId: user.Id.ToString(), Data: new Dictionary<string, object?> { ["provider"] = provider, ["by"] = "admin", ["forced"] = true }), ct);
        return result.Succeeded ? OperationResult.Ok : OperationResult.Fail(result.Errors);
    }

    public async Task<(OperationResult, IReadOnlyList<PasskeyDto>)> ListUserPasskeysAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await MemberAsync(userId, ct);
        if (user is null) return (OperationResult.Fail(Auth2ErrorCodes.NotFound), []);
        return (OperationResult.Ok, [.. (await users.GetPasskeysAsync(user)).OrderBy(p => p.CreatedAt).Select(PasskeyService.ToDto)]);
    }

    public async Task<OperationResult> RemoveUserPasskeyAsync(Guid userId, string passkeyId, CancellationToken ct = default)
    {
        var user = await MemberAsync(userId, ct);
        if (user is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        byte[] bytes;
        try { bytes = System.Buffers.Text.Base64Url.DecodeFromChars(passkeyId); } catch (FormatException) { return OperationResult.Fail(Auth2ErrorCodes.NotFound); }
        if (await users.GetPasskeyAsync(user, bytes) is null) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var result = await users.RemovePasskeyAsync(user, bytes);
        if (result.Succeeded) await audit.WriteAsync(new AuditEntry("passkey.removed", null, TargetType: "user", TargetId: user.Id.ToString(), Data: new Dictionary<string, object?> { ["by"] = "admin" }), ct);
        return result.Succeeded ? OperationResult.Ok : OperationResult.Fail(result.Errors);
    }

    // ------------------------------------------------------------------------------------ blocked models

    public async Task<BlockedAaguidsDto> GetBlockedAaguidsAsync(CancellationToken ct = default)
    {
        var stored = await serverValues.GetAsync(BlockedKey[0], ct);
        var fromDb = string.IsNullOrWhiteSpace(stored) ? [] : stored.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse);
        return new BlockedAaguidsDto([.. options.Value.Passkeys.BlockedAaguids.Concat(fromDb).Distinct()]);
    }

    public async Task<OperationResult> SetBlockedAaguidsAsync(IReadOnlyList<Guid> aaguids, CancellationToken ct = default)
    {
        if (aaguids.Count > 500) return OperationResult.Fail(Auth2ErrorCodes.TooManyItems);
        await serverValues.SetAsync(BlockedKey[0], string.Join(',', aaguids.Distinct()), ct);
        await audit.WriteAsync(new AuditEntry("passkey.blocked_list.updated", null, TargetType: "passkeys", Data: new Dictionary<string, object?> { ["count"] = aaguids.Count }), ct);
        return OperationResult.Ok;
    }
}

/// <inheritdoc />
internal sealed class ServerValues(Auth2DbContext db, TimeProvider clock) : IServerValues
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        await db.ServerValues.AsNoTracking().Where(v => v.Key == key).Select(v => v.Value).FirstOrDefaultAsync(ct);

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var row = await db.ServerValues.FirstOrDefaultAsync(v => v.Key == key, ct);
        if (row is null) db.ServerValues.Add(new ServerValue { Key = key, Value = value, UpdatedAtUtc = clock.GetUtcNow() });
        else { row.Value = value; row.UpdatedAtUtc = clock.GetUtcNow(); }
        await db.SaveChangesAsync(ct);
    }
}
