namespace SystemX.Auth2.Samples.Mvc.Models;

/// <summary>What a demo page shows: the rule that protects it and why you got in.</summary>
public sealed record RuleViewModel(string Title, string Rule, string Explanation, string? UserName);

/// <summary>What the Inspect page shows.</summary>
public sealed record InspectViewModel(string? UserName, string? ClientId, IReadOnlyList<string> Roles, IReadOnlyList<string> Groups, IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Scopes, IReadOnlyList<string> Audiences, IReadOnlyList<string> Amr, IReadOnlyList<(string Check, bool Result)> Checks);
