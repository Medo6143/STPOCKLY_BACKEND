namespace Stockly.Application.Features.Admin.SeedPlatformAdmin;

/// <summary>
/// Configuration section <c>Admin</c> (<c>Admin__Email</c>, <c>Admin__Password</c>, <c>Admin__FullName</c> in <c>.env</c>).
/// The password is a secret: it is never logged. When a value is missing the seed is skipped with a warning.
/// </summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? FullName { get; set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(FullName);

    /// <summary>Never prints the password.</summary>
    public override string ToString() => $"Email set={!string.IsNullOrWhiteSpace(Email)}, FullName set={!string.IsNullOrWhiteSpace(FullName)}";
}
