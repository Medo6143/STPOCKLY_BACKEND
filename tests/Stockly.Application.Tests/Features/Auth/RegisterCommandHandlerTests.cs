using NSubstitute;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Auth.Register;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using static Stockly.Application.Tests.Support.AuthTestContext;
using ContractShopRole = Stockly.Contracts.Shops.ShopRole;

namespace Stockly.Application.Tests.Features.Auth;

public sealed class RegisterCommandHandlerTests
{
    private readonly AuthTestContext _context = new();

    public RegisterCommandHandlerTests()
    {
        _context.Users.CreateAsync(Arg.Any<NewUserAccount>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(UserId);
        _context.Plans.FindActiveIdByCodeAsync(DefaultPlan.Code, Arg.Any<CancellationToken>()).Returns(DefaultPlan.Id);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesShopOwnerSubscriptionAndSession()
    {
        Shop? shop = null;
        _context.Shops.Add(Arg.Do<Shop>(added => shop = added));
        ShopMember? owner = null;
        _context.Memberships.Add(Arg.Do<ShopMember>(added => owner = added));
        ShopSubscription? subscription = null;
        _context.Subscriptions.Add(Arg.Do<ShopSubscription>(added => subscription = added));
        RefreshToken? stored = null;
        _context.RefreshTokens.Add(Arg.Do<RefreshToken>(added => stored = added));

        var result = await Handler().Handle(Command(preferredLanguage: "en"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        shop!.Name.Should().Be("صيدلية الشفاء");
        shop.ShopType.Should().Be(ShopType.Pharmacy);
        owner!.Should().BeEquivalentTo(new { ShopId = shop.Id, UserId, Role = ShopRole.Owner, IsActive = true, JoinedAt = Now });
        subscription!.Should().BeEquivalentTo(new
        {
            ShopId = shop.Id,
            PlanId = DefaultPlan.Id,
            Status = SubscriptionStatus.Active,
            StartsOn = new DateOnly(2026, 10, 12),
            EndsOn = (DateOnly?)null,
        });
        stored!.UserId.Should().Be(UserId);
        stored.DeviceId.Should().Be("pixel-7");
        stored.TokenHash.Should().Be("hash-1");

        var response = result.Value;
        response.RefreshToken.Should().Be("refresh-1");
        response.AccessToken.Should().Be("access-Owner");
        response.RefreshTokenExpiresAt.Should().Be((Now + TokenLifetimes.RefreshToken).UtcDateTime);
        response.User.Should().Be(new UserDto(UserId, "mona@example.com", "Mona Adel", "0100", "en"));
        response.Shop.Id.Should().Be(shop.Id);
        response.Shop.Role.Should().Be(ContractShopRole.Owner);
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_WritesTenantRowsInsideTheNewShopsScope()
    {
        Guid? scopedShop = null;
        _context.TenantScope.BeginForNewShop(Arg.Do<Guid>(id => scopedShop = id));
        Shop? shop = null;
        _context.Shops.Add(Arg.Do<Shop>(added => shop = added));

        await Handler().Handle(Command(), CancellationToken.None);

        scopedShop.Should().Be(shop!.Id);
        Received.InOrder(() =>
        {
            _context.TenantScope.BeginForNewShop(shop.Id);
            _context.Subscriptions.Add(Arg.Any<ShopSubscription>());
            _context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _context.ScopeHandle.Dispose();
        });
    }

    [Fact]
    public async Task Handle_TrimsAndDefaultsUserFields()
    {
        NewUserAccount? account = null;
        await _context.Users.CreateAsync(Arg.Do<NewUserAccount>(a => account = a), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await Handler().Handle(Command(email: " mona@example.com ", phone: "  "), CancellationToken.None);

        account.Should().Be(new NewUserAccount("mona@example.com", "Mona Adel", null, PreferredLanguage.Ar, Now));
    }

    [Theory]
    [InlineData("Bakery")]
    [InlineData("pharmacy")]
    [InlineData("1")]
    public async Task Handle_UnknownShopType_ReturnsUnknownTypeAndWritesNothing(string shopType)
    {
        var result = await Handler().Handle(Command(shopType: shopType), CancellationToken.None);

        result.Error.Should().Be(ShopErrors.UnknownType);
        await _context.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
        _context.Shops.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ًـ")]
    public async Task Handle_ShopNameBlankAfterNormalization_ReturnsNameRequired(string name)
    {
        var result = await Handler().Handle(Command(shopName: name), CancellationToken.None);

        result.Error.Should().Be(ShopErrors.NameRequired);
        await _context.Users.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_EmailTaken_ReturnsConflictAndAddsNoShop()
    {
        _context.Users.CreateAsync(Arg.Any<NewUserAccount>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(AuthErrors.EmailTaken));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.EmailTaken);
        _context.Shops.DidNotReceiveWithAnyArgs().Add(default!);
        _context.TenantScope.DidNotReceiveWithAnyArgs().BeginForNewShop(default);
        await _context.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Handle_DefaultPlanMissing_ThrowsSoTheTransactionRollsBack()
    {
        _context.Plans.FindActiveIdByCodeAsync(DefaultPlan.Code, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var act = () => Handler().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static RegisterCommand Command(
        string email = "mona@example.com",
        string shopName = "صيدلية الشفاء",
        string shopType = "Pharmacy",
        string? preferredLanguage = null,
        string? phone = "0100") =>
        new("Mona Adel", email, "Str0ngPass", phone, preferredLanguage, "pixel-7", new RegisterShopRequest(shopName, shopType, null, "القاهرة", null));

    private RegisterCommandHandler Handler() => new(
        _context.Users,
        _context.Shops,
        _context.Memberships,
        _context.Plans,
        _context.Subscriptions,
        _context.TenantScope,
        _context.Sessions,
        _context.UnitOfWork,
        _context.Clock);
}
