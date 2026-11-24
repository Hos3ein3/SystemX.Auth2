using System.Text.RegularExpressions;
using MimeKit;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;
using SystemX.Auth2.Server.Infrastructure.External;
using SystemX.Auth2.Server.Persistence;

namespace SystemX.Auth2.Server.Infrastructure.Admin;

/// <summary>See <see cref="IAdminMessagingService"/>. The password is encrypted before it is saved and never put into an answer or an audit event.</summary>
internal sealed partial class AdminMessagingService(
    IProjectService projects, IApplicationService applications, IGlobalScope globalScope, IExternalSecretProtector secrets,
    IEmailFactory emailFactory, ISmsFactory smsFactory, IAuditService audit) : IAdminMessagingService
{
    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$", RegexOptions.None, 200)]
    private static partial Regex HostName();

    public async Task<MessagingDto?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        return await projects.FindAsync(projectId, ct) is { } p ? ToDto(p.Settings.Messaging, inherited: false) : null;
    }

    public async Task<OperationResult> UpdateProjectAsync(Guid projectId, MessagingUpdate update, Guid? actorUserId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        if (await projects.FindAsync(projectId, ct) is not { } project) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var settings = SettingsJson.Clone(project.Settings);
        var errors = Apply(settings.Messaging, update);
        if (errors.Count > 0) return AdminSupport.Invalid(errors);
        await projects.UpdateSettingsAsync(projectId, settings, ct);
        await audit.WriteAsync(new AuditEntry("messaging.updated", projectId, actorUserId, TargetType: "project", TargetId: projectId.ToString(),
            Data: Describe(settings.Messaging)), ct);
        return OperationResult.Ok;
    }

    public async Task<MessagingDto?> GetApplicationAsync(Guid projectId, Guid applicationId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        if (await applications.FindAsync(applicationId, ct) is not { } app || app.ProjectId != projectId) return null;
        return app.Settings.Messaging is { } m ? ToDto(m, inherited: false) : ToDto(new MessagingSettings(), inherited: true);
    }

    public async Task<OperationResult> UpdateApplicationAsync(Guid projectId, Guid applicationId, MessagingUpdate update, Guid? actorUserId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        if (await applications.FindAsync(applicationId, ct) is not { } app || app.ProjectId != projectId) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var settings = SettingsJson.Clone(app.Settings);
        settings.Messaging ??= new MessagingSettings();
        var errors = Apply(settings.Messaging, update);
        if (errors.Count > 0) return AdminSupport.Invalid(errors);
        await applications.UpdateAsync(applicationId, new ApplicationSpec("", Settings: settings), ct);
        await audit.WriteAsync(new AuditEntry("messaging.updated", projectId, actorUserId, TargetType: "application", TargetId: applicationId.ToString(),
            Data: Describe(settings.Messaging)), ct);
        return OperationResult.Ok;
    }

    public async Task<OperationResult> ClearApplicationAsync(Guid projectId, Guid applicationId, Guid? actorUserId, CancellationToken ct = default)
    {
        using var _ = globalScope.Begin();
        if (await applications.FindAsync(applicationId, ct) is not { } app || app.ProjectId != projectId) return OperationResult.Fail(Auth2ErrorCodes.NotFound);
        var settings = SettingsJson.Clone(app.Settings);
        settings.Messaging = null;
        await applications.UpdateAsync(applicationId, new ApplicationSpec("", Settings: settings), ct);
        await audit.WriteAsync(new AuditEntry("messaging.cleared", projectId, actorUserId, TargetType: "application", TargetId: applicationId.ToString()), ct);
        return OperationResult.Ok;
    }

    // ------------------------------------------------------------------ helpers

    private MessagingDto ToDto(MessagingSettings m, bool inherited)
    {
        var e = m.Email;
        return new MessagingDto(inherited,
            new EmailProviderDto(e.Provider, e.Host, e.Port, e.Security, e.UserName, !string.IsNullOrEmpty(e.PasswordProtected), e.FromAddress, e.FromName, e.TimeoutSeconds),
            m.Sms, [.. emailFactory.ProviderNames.Order()], [.. smsFactory.ProviderNames.Order()]);
    }

    /// <summary>What goes into the audit event: no password, only whether one is set.</summary>
    private static Dictionary<string, object?> Describe(MessagingSettings m) => new()
    {
        ["emailProvider"] = m.Email.Provider, ["emailHost"] = m.Email.Host, ["emailUserName"] = m.Email.UserName,
        ["emailPasswordIsSet"] = !string.IsNullOrEmpty(m.Email.PasswordProtected), ["smsProvider"] = m.Sms.Provider,
    };

    /// <summary>Applies the change to <paramref name="target"/> and returns what is wrong with the result.</summary>
    private List<SettingsError> Apply(MessagingSettings target, MessagingUpdate update)
    {
        var errors = new List<SettingsError>();
        if (update.Email is { } u)
        {
            var e = target.Email;
            if (u.Provider is not null) e.Provider = u.Provider.Trim().ToLowerInvariant();
            if (u.Host is not null) e.Host = string.IsNullOrWhiteSpace(u.Host) ? null : u.Host.Trim();
            if (u.Port is { } port) e.Port = port;
            if (u.Security is { } sec) e.Security = sec;
            if (u.UserName is not null) e.UserName = string.IsNullOrWhiteSpace(u.UserName) ? null : u.UserName.Trim();
            if (u.Password is not null) e.PasswordProtected = secrets.Protect(u.Password);   // empty text gives null: removed
            if (u.FromAddress is not null) e.FromAddress = string.IsNullOrWhiteSpace(u.FromAddress) ? null : u.FromAddress.Trim();
            if (u.FromName is not null) e.FromName = string.IsNullOrWhiteSpace(u.FromName) ? null : u.FromName.Trim();
            if (u.TimeoutSeconds is { } t) e.TimeoutSeconds = t;
            ValidateEmail(e, errors);
        }
        if (update.Sms is { } s)
        {
            target.Sms.Provider = s.Provider.Trim().ToLowerInvariant();
            target.Sms.Sender = string.IsNullOrWhiteSpace(s.Sender) ? null : s.Sender.Trim();
            if (target.Sms.Provider.Length > 0 && !smsFactory.ProviderNames.Contains(target.Sms.Provider, StringComparer.OrdinalIgnoreCase))
                errors.Add(new SettingsError("Messaging.Sms.Provider", $"Unknown provider. Known: {string.Join(", ", smsFactory.ProviderNames.Order())}."));
        }
        return errors;
    }

    private void ValidateEmail(EmailProviderSettings e, List<SettingsError> errors)
    {
        const string path = "Messaging.Email";
        if (e.Provider.Length > 0 && !emailFactory.ProviderNames.Contains(e.Provider, StringComparer.OrdinalIgnoreCase))
            errors.Add(new SettingsError($"{path}.Provider", $"Unknown provider. Known: {string.Join(", ", emailFactory.ProviderNames.Order())}."));
        if (e.Port is < 1 or > 65535) errors.Add(new SettingsError($"{path}.Port", "Must be between 1 and 65535."));
        if (e.TimeoutSeconds is < 1 or > 120) errors.Add(new SettingsError($"{path}.TimeoutSeconds", "Must be between 1 and 120."));
        if (e.FromAddress is not null && !(MailboxAddress.TryParse(e.FromAddress, out var from) && from is not null))
            errors.Add(new SettingsError($"{path}.FromAddress", "Is not a valid email address."));

        var smtp = e.Provider is EmailProviders.Smtp;
        var google = e.Provider is EmailProviders.Google;
        if (smtp)
        {
            if (string.IsNullOrEmpty(e.Host)) errors.Add(new SettingsError($"{path}.Host", "Is required for smtp."));
            else if (!HostName().IsMatch(e.Host)) errors.Add(new SettingsError($"{path}.Host", "Must be a host name or an IP address, without a scheme or a path."));
            if (string.IsNullOrEmpty(e.FromAddress)) errors.Add(new SettingsError($"{path}.FromAddress", "Is required for smtp."));
            if (!string.IsNullOrEmpty(e.UserName) && string.IsNullOrEmpty(e.PasswordProtected)) errors.Add(new SettingsError($"{path}.Password", "Is required when a user name is set."));
        }
        if (google)
        {
            if (string.IsNullOrEmpty(e.UserName)) errors.Add(new SettingsError($"{path}.UserName", "Is required for google: the Gmail address."));
            if (string.IsNullOrEmpty(e.PasswordProtected)) errors.Add(new SettingsError($"{path}.Password", "Is required for google: an app password."));
        }
    }
}
