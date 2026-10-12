using NSubstitute;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;

namespace Stockly.Application.Tests.Support;

/// <summary>Substitutes for every port the auth handlers use, with a fixed clock and predictable tokens.</summary>
internal sealed class AuthTestContext
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);
    public static readonly Guid UserId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000a1");
    public static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");
    public static readonly UserAccount User = new(UserId, "mona@example.com", "Mona Adel", "0100", PreferredLanguage.Ar);
    public static readonly ShopMembership Membership =
        new(UserId, ShopId, "صيدلية الشفاء", ShopType.Pharmacy, ShopStatus.Active, ShopRole.Owner, Now.AddDays(-10));

    private int _refreshCounter;

    public AuthTestContext()
    {
        Clock.UtcNow.Returns(Now);
        Tokens.CreateAccessToken(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ShopRole>())
            .Returns(call => new AccessToken($"access-{call.ArgAt<ShopRole>(2)}", Now + TokenLifetimes.AccessToken));
        Tokens.CreateRefreshToken().Returns(_ =>
        {
            var number = Interlocked.Increment(ref _refreshCounter);
            return new RefreshTokenValue($"refresh-{number}", $"hash-{number}");
        });
        Tokens.HashRefreshToken(Arg.Any<string>()).Returns(call => "hash-of-" + call.Arg<string>());
        TenantScope.BeginForNewShop(Arg.Any<Guid>()).Returns(ScopeHandle);
        Sessions = new AuthSessions(Tokens, RefreshTokens, Clock);
    }

    public IClock Clock { get; } = Substitute.For<IClock>();

    public ITokenService Tokens { get; } = Substitute.For<ITokenService>();

    public IUserAccounts Users { get; } = Substitute.For<IUserAccounts>();

    public IShopRepository Shops { get; } = Substitute.For<IShopRepository>();

    public IShopMembershipRepository Memberships { get; } = Substitute.For<IShopMembershipRepository>();

    public IPlanRepository Plans { get; } = Substitute.For<IPlanRepository>();

    public IShopSubscriptionRepository Subscriptions { get; } = Substitute.For<IShopSubscriptionRepository>();

    public IRefreshTokenRepository RefreshTokens { get; } = Substitute.For<IRefreshTokenRepository>();

    public ITenantScope TenantScope { get; } = Substitute.For<ITenantScope>();

    public IDisposable ScopeHandle { get; } = Substitute.For<IDisposable>();

    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();

    public ICurrentTenant CurrentTenant { get; } = Substitute.For<ICurrentTenant>();

    public AuthSessions Sessions { get; }

    public static RefreshToken StoredToken(DateTimeOffset? createdAt = null, Guid? userId = null) =>
        RefreshToken.StartFamily(userId ?? UserId, "hash-of-presented", "pixel-7", createdAt ?? Now.AddDays(-1), TokenLifetimes.RefreshToken);
}
