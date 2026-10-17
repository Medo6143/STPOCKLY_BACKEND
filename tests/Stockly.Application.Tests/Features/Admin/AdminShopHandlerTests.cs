using System.Globalization;
using System.Text.Json;
using NSubstitute;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Admin.ActivateShop;
using Stockly.Application.Features.Admin.ChangeSubscription;
using Stockly.Application.Features.Admin.GetShop;
using Stockly.Application.Features.Admin.GetShops;
using Stockly.Application.Features.Admin.ResetScanQuota;
using Stockly.Application.Features.Admin.SetQuotaOverride;
using Stockly.Application.Features.Admin.SuspendShop;
using Stockly.Application.Features.Auth;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using static Stockly.Application.Tests.Support.AdminTestContext;

namespace Stockly.Application.Tests.Features.Admin;

/// <summary>AD4–AD10: every write audited once in its own transaction, none on idempotent repeats.</summary>
public sealed class AdminShopHandlerTests
{
    private readonly AdminTestContext _context = new();
    private readonly Shop _shop = NewShop();

    public AdminShopHandlerTests()
    {
        _context.Shops.FindAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(_shop);
    }

    [Fact]
    public async Task Suspend_ActiveShop_SuspendsRevokesTokensAndAuditsOnce()
    {
        _context.Shops.RevokeMemberRefreshTokensAsync(_shop.Id, Now, Arg.Any<CancellationToken>()).Returns(3);

        var result = await Suspend(_shop.Id, " unpaid ");

        result.IsSuccess.Should().BeTrue();
        _shop.Status.Should().Be(ShopStatus.Suspended);
        _shop.SuspendedReason.Should().Be("unpaid");
        await _context.Shops.Received(1).RevokeMemberRefreshTokensAsync(_shop.Id, Now, Arg.Any<CancellationToken>());
        var entry = _context.Audited.Should().ContainSingle().Subject;
        entry.Should().BeEquivalentTo(new
        {
            AdminUserId = AdminId,
            Action = AdminActions.ShopSuspend,
            TargetType = AdminTargets.Shop,
            TargetId = _shop.Id,
            Reason = "unpaid",
            CreatedAt = Now,
        });
        JsonDocument.Parse(entry.Before!).RootElement.GetProperty("status").GetString().Should().Be("Active");
        JsonDocument.Parse(entry.After!).RootElement.GetProperty("status").GetString().Should().Be("Suspended");
        JsonDocument.Parse(entry.After!).RootElement.GetProperty("revokedRefreshTokens").GetInt32().Should().Be(3);
        _context.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Suspend_AlreadySuspended_SucceedsWithoutAuditOrRevocation()
    {
        _shop.Suspend("earlier", Now.AddDays(-1));

        var result = await Suspend(_shop.Id, "again");

        result.IsSuccess.Should().BeTrue();
        _context.Audited.Should().BeEmpty();
        await _context.Shops.DidNotReceiveWithAnyArgs().RevokeMemberRefreshTokensAsync(default, default, default);
        _context.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Suspend_BlankReason_IsReasonRequired(string? reason)
    {
        (await Suspend(_shop.Id, reason)).Error.Should().Be(AdminErrors.ReasonRequired);
        _shop.Status.Should().Be(ShopStatus.Active);
    }

    [Fact]
    public async Task Suspend_UnknownShop_IsShopNotFound()
    {
        (await Suspend(Guid.CreateVersion7(), "x")).Error.Should().Be(ShopErrors.NotFound);
        _context.Audited.Should().BeEmpty();
    }

    [Fact]
    public async Task Suspend_NoUser_IsUnauthenticated()
    {
        _context.CurrentUser.UserId.Returns((Guid?)null);

        (await Suspend(_shop.Id, "x")).Error.Should().Be(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task Activate_SuspendedShop_ActivatesAndAuditsTheSuspension()
    {
        _shop.Suspend("unpaid", Now.AddDays(-1));

        var result = await Activate(_shop.Id, "paid");

        result.IsSuccess.Should().BeTrue();
        _shop.Status.Should().Be(ShopStatus.Active);
        var entry = _context.Audited.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AdminActions.ShopActivate);
        JsonDocument.Parse(entry.Before!).RootElement.GetProperty("suspendedReason").GetString().Should().Be("unpaid");
        _context.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Activate_ActiveShop_SucceedsWithoutAudit()
    {
        (await Activate(_shop.Id, "x")).IsSuccess.Should().BeTrue();
        _context.Audited.Should().BeEmpty();
    }

    [Fact]
    public async Task Activate_UnknownShopOrBlankReason_Fails()
    {
        (await Activate(Guid.CreateVersion7(), "x")).Error.Should().Be(ShopErrors.NotFound);
        (await Activate(_shop.Id, "")).Error.Should().Be(AdminErrors.ReasonRequired);
    }

    [Fact]
    public async Task ChangeSubscription_ActivePlan_EndsTheCurrentStartsTheNewCarryingTheOverride()
    {
        var plan = Plan.Create("pro", "برو", "Pro", 100, 5, 99m).Value;
        _context.Plans.FindAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        var current = NewSubscription(_shop.Id, quotaOverride: 70);
        _context.Shops.FindActiveSubscriptionAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(current);
        ShopSubscription? started = null;
        await _context.Shops.AddSubscriptionAsync(Arg.Do<ShopSubscription>(subscription => started = subscription), Arg.Any<CancellationToken>());
        var dto = SubscriptionDto(plan.Id, 70);
        _context.ShopQueries.FindSubscriptionAsync(_shop.Id, Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await ChangeSubscription(plan.Id, Today.AddDays(30));

        result.Value.Should().Be(dto);
        current.Status.Should().Be(SubscriptionStatus.Ended);
        current.EndsOn.Should().Be(Today);
        started!.PlanId.Should().Be(plan.Id);
        started.StartsOn.Should().Be(Today);
        started.EndsOn.Should().Be(Today.AddDays(30));
        started.DailyScanQuotaOverride.Should().Be(70);
        started.Status.Should().Be(SubscriptionStatus.Active);
        var entry = _context.Audited.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AdminActions.ShopChangeSubscription);
        JsonDocument.Parse(entry.Before!).RootElement.GetProperty("planId").GetGuid().Should().Be(DefaultPlan.Id);
        JsonDocument.Parse(entry.After!).RootElement.GetProperty("planCode").GetString().Should().Be("pro");
        _context.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task ChangeSubscription_UnknownPlan_IsPlanNotFound()
    {
        (await ChangeSubscription(Guid.CreateVersion7(), null)).Error.Should().Be(PlanErrors.NotFound);
    }

    [Fact]
    public async Task ChangeSubscription_InactivePlan_IsPlanInactive()
    {
        var plan = Plan.Create("old", "قديم", "Old", 10, 1, 0m, isActive: false).Value;
        _context.Plans.FindAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);

        (await ChangeSubscription(plan.Id, null)).Error.Should().Be(PlanErrors.Inactive);
        await _context.Shops.DidNotReceiveWithAnyArgs().AddSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task ChangeSubscription_UnknownShop_IsShopNotFound()
    {
        var result = await new ChangeSubscriptionCommandHandler(
                _context.CurrentUser, _context.Shops, _context.ShopQueries, _context.Plans, _context.Audit, _context.UnitOfWork, _context.Clock)
            .Handle(new ChangeSubscriptionCommand(Guid.CreateVersion7(), DefaultPlan.Id, null, null), CancellationToken.None);

        result.Error.Should().Be(ShopErrors.NotFound);
    }

    [Fact]
    public void ChangeSubscriptionValidator_EndBeforeToday_IsInvalid()
    {
        var validator = new ChangeSubscriptionCommandValidator(_context.Clock);

        validator.Validate(new ChangeSubscriptionCommand(_shop.Id, DefaultPlan.Id, Today.AddDays(-1), null)).IsValid.Should().BeFalse();
        validator.Validate(new ChangeSubscriptionCommand(_shop.Id, DefaultPlan.Id, Today, null)).IsValid.Should().BeTrue();
        validator.Validate(new ChangeSubscriptionCommand(_shop.Id, Guid.Empty, null, null)).IsValid.Should().BeFalse();
        validator.Validate(new ChangeSubscriptionCommand(_shop.Id, DefaultPlan.Id, null, new string('x', 501))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task SetQuotaOverride_NewValue_SetsAuditsAndReturnsTheSubscription()
    {
        var subscription = NewSubscription(_shop.Id);
        _context.Shops.FindActiveSubscriptionAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(subscription);
        var dto = SubscriptionDto(DefaultPlan.Id, 200);
        _context.ShopQueries.FindSubscriptionAsync(_shop.Id, Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await SetOverride(200, "big order");

        result.Value.Should().Be(dto);
        subscription.DailyScanQuotaOverride.Should().Be(200);
        var entry = _context.Audited.Should().ContainSingle().Subject;
        entry.Before.Should().Be("""{"dailyScanQuotaOverride":null}""");
        entry.After.Should().Be("""{"dailyScanQuotaOverride":200}""");
        _context.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task SetQuotaOverride_SameValue_NoAudit()
    {
        _context.Shops.FindActiveSubscriptionAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(NewSubscription(_shop.Id, 50));
        _context.ShopQueries.FindSubscriptionAsync(_shop.Id, Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionDto(DefaultPlan.Id, 50));

        (await SetOverride(50, "same")).IsSuccess.Should().BeTrue();

        _context.Audited.Should().BeEmpty();
        _context.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SetQuotaOverride_UnknownShopOrBlankReason_Fails()
    {
        (await SetOverride(5, " ")).Error.Should().Be(AdminErrors.ReasonRequired);
        var unknown = await new SetQuotaOverrideCommandHandler(_context.CurrentUser, _context.Shops, _context.ShopQueries, _context.Audit, _context.UnitOfWork)
            .Handle(new SetQuotaOverrideCommand(Guid.CreateVersion7(), 5, "x"), CancellationToken.None);
        unknown.Error.Should().Be(ShopErrors.NotFound);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    [InlineData(null, true)]
    public void SetQuotaOverrideValidator_Range(int? value, bool valid)
    {
        new SetQuotaOverrideCommandValidator().Validate(new SetQuotaOverrideCommand(_shop.Id, value, "x")).IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task ResetQuota_AuditsThenResetsTheCounterAfterTheSave()
    {
        var order = new List<string>();
        _context.AuditLog.When(log => log.Add(Arg.Any<Domain.Platform.AdminAuditLog>())).Do(_ => order.Add("audit"));
        _context.Quota.ResetAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add($"reset after {_context.UnitOfWork.SaveCount} save");
            return true;
        });

        var result = await new ResetScanQuotaCommandHandler(_context.CurrentUser, _context.Shops, _context.Quota, _context.Audit, _context.UnitOfWork)
            .Handle(new ResetScanQuotaCommand(_shop.Id, "support ticket 12"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Should().Equal("audit", "reset after 1 save");
        _context.Audited.Should().ContainSingle().Which.Action.Should().Be(AdminActions.ShopResetQuota);
    }

    [Fact]
    public async Task ResetQuota_RedisDown_StillSucceeds()
    {
        _context.Quota.ResetAsync(_shop.Id, Arg.Any<CancellationToken>()).Returns(false);

        var result = await new ResetScanQuotaCommandHandler(_context.CurrentUser, _context.Shops, _context.Quota, _context.Audit, _context.UnitOfWork)
            .Handle(new ResetScanQuotaCommand(_shop.Id, "x"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ResetQuota_UnknownShopOrBlankReason_FailsWithoutReset()
    {
        var handler = new ResetScanQuotaCommandHandler(_context.CurrentUser, _context.Shops, _context.Quota, _context.Audit, _context.UnitOfWork);

        (await handler.Handle(new ResetScanQuotaCommand(Guid.CreateVersion7(), "x"), CancellationToken.None)).Error.Should().Be(ShopErrors.NotFound);
        (await handler.Handle(new ResetScanQuotaCommand(_shop.Id, null), CancellationToken.None)).Error.Should().Be(AdminErrors.ReasonRequired);
        await _context.Quota.DidNotReceiveWithAnyArgs().ResetAsync(default, default);
    }

    [Fact]
    public async Task GetShop_Unknown_IsShopNotFound_Known_IsTheDetail()
    {
        var handler = new GetShopQueryHandler(_context.ShopQueries, _context.Clock);
        var detail = new AdminShopDetail(
            _shop.Id, _shop.Name, Contracts.Shops.ShopType.Pharmacy, Contracts.Shops.ShopStatus.Active, null, null, null,
            Now.UtcDateTime, null, null, [], null, new AdminShopCounts(0, 0, 0), 0, "0.00000000");
        var dayStart = new DateTimeOffset(2026, 1, 14, 22, 0, 0, TimeSpan.Zero);
        _context.ShopQueries.FindDetailAsync(_shop.Id, dayStart, Now.AddDays(-30), Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>())
            .Returns(detail);

        (await handler.Handle(new GetShopQuery(Guid.CreateVersion7()), CancellationToken.None)).Error.Should().Be(ShopErrors.NotFound);
        (await handler.Handle(new GetShopQuery(_shop.Id), CancellationToken.None)).Value.Should().Be(detail);
    }

    [Fact]
    public async Task GetShops_Search_IsNormalizedForTheNameAndUpperCasedForTheEmail()
    {
        var page = new PagedResponse<AdminShopListItem>([], 2, 10, 0);
        _context.ShopQueries.ListAsync(
                "صيدليه", "صَيدلية", Domain.Shops.ShopStatus.Suspended, 2, 10, Arg.Any<DateTimeOffset>(), PreferredLanguage.En, Arg.Any<CancellationToken>())
            .Returns(page);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        try
        {
            var result = await new GetShopsQueryHandler(_context.ShopQueries, _context.Clock)
                .Handle(new GetShopsQuery(" صَيدلية ", Contracts.Shops.ShopStatus.Suspended, 2, 10), CancellationToken.None);

            result.Value.Should().BeSameAs(page);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task GetShops_EmailSearch_IsUpperCasedPrefix()
    {
        await new GetShopsQueryHandler(_context.ShopQueries, _context.Clock)
            .Handle(new GetShopsQuery("Mona@", null, 1, 20), CancellationToken.None);

        await _context.ShopQueries.Received(1).ListAsync(
            "mona@", "MONA@", null, 1, 20, Arg.Any<DateTimeOffset>(), Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 20, false)]
    [InlineData(1, 101, false)]
    [InlineData(1, 100, true)]
    public void GetShopsValidator_Paging(int page, int pageSize, bool valid)
    {
        new GetShopsQueryValidator().Validate(new GetShopsQuery(null, null, page, pageSize)).IsValid.Should().Be(valid);
    }

    [Fact]
    public void ReasonValidators_RejectMoreThan500Characters()
    {
        var reason = new string('x', 501);

        new SuspendShopCommandValidator().Validate(new SuspendShopCommand(_shop.Id, reason)).IsValid.Should().BeFalse();
        new ActivateShopCommandValidator().Validate(new ActivateShopCommand(_shop.Id, reason)).IsValid.Should().BeFalse();
        new ResetScanQuotaCommandValidator().Validate(new ResetScanQuotaCommand(_shop.Id, reason)).IsValid.Should().BeFalse();
        new SuspendShopCommandValidator().Validate(new SuspendShopCommand(_shop.Id, new string('x', 500))).IsValid.Should().BeTrue();
    }

    private Task<Domain.Common.Result> Suspend(Guid shopId, string? reason) =>
        new SuspendShopCommandHandler(_context.CurrentUser, _context.Shops, _context.Audit, _context.UnitOfWork, _context.Clock)
            .Handle(new SuspendShopCommand(shopId, reason), CancellationToken.None);

    private Task<Domain.Common.Result> Activate(Guid shopId, string? reason) =>
        new ActivateShopCommandHandler(_context.CurrentUser, _context.Shops, _context.Audit, _context.UnitOfWork)
            .Handle(new ActivateShopCommand(shopId, reason), CancellationToken.None);

    private Task<Domain.Common.Result<AdminSubscriptionDto>> ChangeSubscription(Guid planId, DateOnly? endsOn) =>
        new ChangeSubscriptionCommandHandler(
                _context.CurrentUser, _context.Shops, _context.ShopQueries, _context.Plans, _context.Audit, _context.UnitOfWork, _context.Clock)
            .Handle(new ChangeSubscriptionCommand(_shop.Id, planId, endsOn, "upgrade"), CancellationToken.None);

    private Task<Domain.Common.Result<AdminSubscriptionDto>> SetOverride(int? value, string? reason) =>
        new SetQuotaOverrideCommandHandler(_context.CurrentUser, _context.Shops, _context.ShopQueries, _context.Audit, _context.UnitOfWork)
            .Handle(new SetQuotaOverrideCommand(_shop.Id, value, reason), CancellationToken.None);
}
