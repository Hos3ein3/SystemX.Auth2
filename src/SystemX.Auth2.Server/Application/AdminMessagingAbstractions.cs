using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Application;

/// <summary>The email provider as the admin sees it. The password is never returned.</summary>
public sealed record EmailProviderDto(string Provider, string? Host, int Port, SmtpSecurity Security, string? UserName, bool PasswordIsSet,
    string? FromAddress, string? FromName, int TimeoutSeconds);

/// <summary>The messaging settings of a project or an application, with the provider names the server has.</summary>
public sealed record MessagingDto(bool Inherited, EmailProviderDto Email, SmsProviderSettings Sms,
    IReadOnlyCollection<string> EmailProviders, IReadOnlyCollection<string> SmsProviders);

/// <summary>
/// A change of the email provider. <c>Password</c>: null keeps the saved password, an empty text removes it, any other text replaces it.
/// Fields that are null keep their value.
/// </summary>
public sealed record EmailProviderUpdate(string? Provider, string? Host, int? Port, SmtpSecurity? Security, string? UserName, string? Password,
    string? FromAddress, string? FromName, int? TimeoutSeconds);

/// <summary>A change of the messaging settings. A null part is left as it is.</summary>
public sealed record MessagingUpdate(EmailProviderUpdate? Email, SmsProviderSettings? Sms);

/// <summary>Reads and changes how a project or an application sends email and SMS. Only a global admin may change it.</summary>
public interface IAdminMessagingService
{
    /// <summary>Settings of a project. Null when the project does not exist.</summary>
    Task<MessagingDto?> GetProjectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Changes the settings of a project.</summary>
    Task<OperationResult> UpdateProjectAsync(Guid projectId, MessagingUpdate update, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Settings of one application (<c>Inherited</c> when it has none of its own). Null when it does not exist.</summary>
    Task<MessagingDto?> GetApplicationAsync(Guid projectId, Guid applicationId, CancellationToken ct = default);

    /// <summary>Changes the settings of one application.</summary>
    Task<OperationResult> UpdateApplicationAsync(Guid projectId, Guid applicationId, MessagingUpdate update, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Removes the own settings of an application, so it uses the project's again.</summary>
    Task<OperationResult> ClearApplicationAsync(Guid projectId, Guid applicationId, Guid? actorUserId, CancellationToken ct = default);
}
