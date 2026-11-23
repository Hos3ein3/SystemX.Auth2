namespace SystemX.Auth2.Contracts.Applications;

/// <summary>Update an application.</summary>
public sealed record UpdateApplicationRequest(string Name, string? Description, bool IsEnabled);
