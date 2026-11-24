namespace SystemX.Auth2.Server.Domain.Entities;

/// <summary>
/// A password a person used before. Only the <b>hash</b> is stored, made by the same hasher as the real password.
/// <see cref="CredentialProjectId"/> is the project when credentials are per project, and null when they are shared (one password for all projects).
/// It is not called <c>ProjectId</c> on purpose: the context fills a null <c>ProjectId</c> with the current project when it saves.
/// </summary>
public class PasswordHistoryEntry
{
    /// <summary>Id.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>The person.</summary>
    public Guid UserId { get; set; }
    /// <summary>The project whose credentials this is, or null for shared credentials.</summary>
    public Guid? CredentialProjectId { get; set; }
    /// <summary>The hash of the old password.</summary>
    public string PasswordHash { get; set; } = "";
    /// <summary>When the password was replaced (saved here).</summary>
    public DateTimeOffset SavedAtUtc { get; set; }
}
