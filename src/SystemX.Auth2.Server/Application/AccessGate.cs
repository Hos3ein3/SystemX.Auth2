using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Domain.Entities;

namespace SystemX.Auth2.Server.Application;

/// <summary>
/// The one rule that decides if a project and an application may be used at all.
/// A disabled project blocks everybody in every application. A disabled application blocks only itself.
/// Order: the project first, then the application.
/// </summary>
public static class AccessGate
{
    /// <summary>Returns <c>project_disabled</c>, <c>application_disabled</c>, or null when the request may go on.</summary>
    public static string? Check(Project project, ClientApplicationInfo? application) =>
        !project.IsEnabled ? Auth2ErrorCodes.ProjectDisabled
        : application is { IsEnabled: false } ? Auth2ErrorCodes.ApplicationDisabled
        : null;
}
