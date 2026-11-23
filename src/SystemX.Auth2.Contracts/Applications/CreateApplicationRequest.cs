namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Create an application. Only a global admin may do this.</summary>
public sealed record CreateApplicationRequest(string ProjectKey, string Name, string? Description = null, string? ClientId = null);
