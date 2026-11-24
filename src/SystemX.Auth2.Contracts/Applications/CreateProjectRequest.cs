namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Create a project. Only a global admin may do this. The project key never changes afterwards.</summary>
public sealed record CreateProjectRequest(string ProjectKey, string Name, string? Description = null);
