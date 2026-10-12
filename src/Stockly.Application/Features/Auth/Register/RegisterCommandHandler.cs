using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Auth.Register;

public sealed class RegisterCommandHandler(
    IUserAccounts users,
    IShopRepository shops,
    IShopMembershipRepository memberships,
    IPlanRepository plans,
    IShopSubscriptionRepository subscriptions,
    ITenantScope tenantScope,
    AuthSessions sessions,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!AuthMapping.TryParseShopType(request.Shop.ShopType, out var shopType))
        {
            return ShopErrors.UnknownType;
        }

        var now = clock.UtcNow;
        var shop = Shop.Create(request.Shop.Name, shopType, request.Shop.Phone, request.Shop.Governorate, request.Shop.Address, now);
        if (shop.IsFailure)
        {
            return shop.Error;
        }

        var account = new NewUserAccount(
            request.Email.Trim(),
            request.FullName.Trim(),
            string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            AuthMapping.ToPreferredLanguage(request.PreferredLanguage),
            now);
        var userId = await users.CreateAsync(account, request.Password, cancellationToken);
        if (userId.IsFailure)
        {
            return userId.Error;
        }

        var planId = await plans.FindActiveIdByCodeAsync(DefaultPlan.Code, cancellationToken)
            ?? throw new InvalidOperationException($"The default plan '{DefaultPlan.Code}' is missing; apply the migrations.");

        // The new shop has no token yet: its tenant rows are written inside an explicit tenant scope (ADR-0002).
        using (tenantScope.BeginForNewShop(shop.Value.Id))
        {
            var owner = ShopMember.Create(shop.Value.Id, userId.Value, ShopRole.Owner, now).Value;
            var subscription = ShopSubscription.StartOpenEnded(shop.Value.Id, planId, clock.Today(), now).Value;

            shops.Add(shop.Value);
            memberships.Add(owner);
            subscriptions.Add(subscription);

            var user = new UserAccount(userId.Value, account.Email, account.FullName, account.Phone, account.PreferredLanguage);
            var membership = new ShopMembership(
                userId.Value, shop.Value.Id, shop.Value.Name, shop.Value.ShopType, shop.Value.Status, owner.Role, owner.JoinedAt);
            var response = sessions.Start(user, membership, request.DeviceId);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return response;
        }
    }
}
