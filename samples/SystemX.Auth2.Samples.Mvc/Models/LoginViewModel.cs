using System.ComponentModel.DataAnnotations;

namespace SystemX.Auth2.Samples.Mvc.Models;

/// <summary>The sign-in form of the sample (Self UI).</summary>
public sealed class LoginViewModel
{
    [Required, Display(Name = "User name or email")] public string Login { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [Display(Name = "Keep me signed in")] public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
