using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin.DowngradeLapsedSubscriptions;

/// <summary>
/// Up to <see cref="AdminLimits.DowngradeBatchSize"/> shops per run (the next run continues). Per shop, in one
/// transaction: lock the active subscription, let it lapse (its last day is kept; a payment that extended it meanwhile
/// makes this a no-op), and start the default plan from today.
/// </summary>
public sealed class DowngradeLapsedSubscriptionsCommandHandler(
    IAdminPaymentRepository payments,
    IAdminShopRepository shops,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<DowngradeLapsedSubscriptionsCommand, Result<int>>
{
    public async Task<Result<int>> Handle(DowngradeLapsedSubscriptionsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = clock.Today();
        var due = await payments.ListShopsWithLapsedSubscriptionAsync(today, DefaultPlan.Id, AdminLimits.DowngradeBatchSize, cancellationToken);
        var moved = 0;
        foreach (var shopId in due)
        {
            var downgraded = await unitOfWork.ExecuteInTransactionAsync(
                token => DowngradeAsync(shopId, today, token),
                done => done,
                cancellationToken);
            moved += downgraded ? 1 : 0;
        }

        return moved;
    }

    private async Task<bool> DowngradeAsync(Guid shopId, DateOnly today, CancellationToken cancellationToken)
    {
        var current = await payments.FindActiveSubscriptionForUpdateAsync(shopId, cancellationToken);
        if (current is null || current.PlanId == DefaultPlan.Id || current.Lapse(today).IsFailure)
        {
            return false;
        }

        var free = ShopSubscription.Start(shopId, DefaultPlan.Id, today, null, current.DailyScanQuotaOverride, clock.UtcNow);
        if (free.IsFailure)
        {
            throw new InvalidOperationException($"The default subscription could not be started: {free.Error.Code}.");
        }

        await shops.AddSubscriptionAsync(free.Value, cancellationToken);
        return true;
    }
}
