namespace SystemX.Auth2.Samples.Mvc.Models;

public class UserProfileViewModel
{
    public bool IsAuthenticated { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];
    public IReadOnlyList<string> Groups { get; set; } = [];

    public string? ClientId { get; set; }
    public string? ProjectKey { get; set; }
    public IReadOnlyList<string> Scopes { get; set; } = [];
    public bool IsMachine { get; set; }
    public string? IdentityProvider { get; set; }
    public IReadOnlyList<string> Amr { get; set; } = [];
    public string? SessionId { get; set; }
}
