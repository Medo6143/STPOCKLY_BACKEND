using Microsoft.AspNetCore.Identity;
using Stockly.Domain.Users;

namespace Stockly.Infrastructure.Identity;

/// <summary>
/// The ASP.NET Core Identity user, stored in <c>users</c> (docs/erd.md §3.1). It lives in Infrastructure because it
/// derives from an Identity type; Application sees it only as <c>UserAccount</c> through <c>IUserAccounts</c>.
/// <c>UserName</c> is the email. <c>PhoneNumber</c> is mapped to the <c>phone</c> column.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    public PreferredLanguage PreferredLanguage { get; set; } = PreferredLanguage.Ar;

    public PlatformRole PlatformRole { get; set; } = PlatformRole.None;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}
