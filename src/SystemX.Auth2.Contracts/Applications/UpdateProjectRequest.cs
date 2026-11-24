namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Update a project. The project key cannot be changed. Disabling a project blocks every application of it.</summary>
public sealed record UpdateProjectRequest(string Name, string? Description, bool IsEnabled, string? Reason = null);
