namespace SystemX.Auth2.Contracts.Access;

/// <summary>A group of users.</summary>
public sealed record GroupDefinition(string Name, string? DisplayName = null);
