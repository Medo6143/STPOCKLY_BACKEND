using Stockly.Application.Abstractions.Messaging;

namespace Stockly.Application.Features.Admin.SeedPlatformAdmin;

/// <summary>
/// Creates the first platform admin at start-up from configuration (<c>Admin__Email</c>, <c>Admin__Password</c>,
/// <c>Admin__FullName</c>, contract §11.1) when no platform admin exists. Never promotes an existing user. Sent by the
/// Infrastructure start-up seeder only; there is no endpoint.
/// </summary>
public sealed record SeedPlatformAdminCommand(string Email, string Password, string FullName) : ICommand<SeedPlatformAdminOutcome>;

/// <summary>What the seed did. <see cref="UserId"/> is the new admin when <see cref="Created"/>.</summary>
public sealed record SeedPlatformAdminOutcome(bool Created, Guid? UserId)
{
    public static SeedPlatformAdminOutcome AlreadyExists { get; } = new(false, null);
}
