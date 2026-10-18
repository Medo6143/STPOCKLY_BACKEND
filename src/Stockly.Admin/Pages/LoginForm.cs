using System.ComponentModel.DataAnnotations;

namespace Stockly.Admin.Pages;

/// <summary>The login form (AD1). Only checks that both fields are filled; the API decides the rest.</summary>
public sealed class LoginForm
{
    [Required(ErrorMessage = "Enter your email.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = string.Empty;
}
