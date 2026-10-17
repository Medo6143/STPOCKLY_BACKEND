using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using Stockly.Infrastructure.Tests.Support;
using ContractShopRole = Stockly.Contracts.Shops.ShopRole;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// The platform admin's cross-tenant persistence (ADR-0005 update 2026-10-18) on a real PostgreSQL. Every scope here has
/// no tenant, like an admin request: the admin ports must still see every shop's rows.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class AdminPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset DayStart = TestTime.T0.AddHours(-9); // 2026-10-11T22:00Z = Cairo midnight (UTC+2)

    [Fact]
    public async Task ShopList_SeesEveryShopWithOwnerPlanAndTodaysScans()
    {
        var marker = Marker();
        var (shop, owner) = await ShopWithOwnerAsync($"صيدلية {marker}");
        await SubscribeAsync(shop.Id);
        await InvoiceAsync(shop.Id, InvoiceStatus.NeedsReview, DayStart.AddMinutes(1));
        await InvoiceAsync(shop.Id, InvoiceStatus.Failed, DayStart.AddMinutes(2));
        await InvoiceAsync(shop.Id, InvoiceStatus.NeedsReview, DayStart.AddMinutes(-1));

        var page = await ShopQueries().ListAsync(marker, null, null, 1, 20, DayStart, PreferredLanguage.Ar, CancellationToken.None);

        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            shop.Id,
            Name = $"صيدلية {marker}",
            OwnerEmail = await EmailOfAsync(owner),
            PlanCode = DefaultPlan.Code,
            ScansToday = 2,
        });
    }

    [Fact]
    public async Task ShopList_SearchIsArabicNormalizedContainsOrOwnerEmailPrefix()
    {
        var marker = Marker();
        var (byName, _) = await ShopWithOwnerAsync($"صيدلية الشفاء {marker}");
        var (byEmail, owner) = await ShopWithOwnerAsync("بقالة");
        var email = await EmailOfAsync(owner);
        var queries = ShopQueries();

        var nameHit = await queries.ListAsync($"صيدليه الشفاء {marker}", null, null, 1, 20, DayStart, PreferredLanguage.Ar, CancellationToken.None);
        var emailHit = await queries.ListAsync(null, email[..20].ToUpperInvariant(), null, 1, 20, DayStart, PreferredLanguage.Ar, CancellationToken.None);
        var middleOfEmail = await queries.ListAsync(null, email[5..20].ToUpperInvariant(), null, 1, 20, DayStart, PreferredLanguage.Ar, CancellationToken.None);
        var wildcard = await queries.ListAsync($"%{marker}", null, null, 1, 20, DayStart, PreferredLanguage.Ar, CancellationToken.None);

        nameHit.Items.Select(item => item.Id).Should().Equal(byName.Id);
        emailHit.Items.Select(item => item.Id).Should().Equal(byEmail.Id);
        middleOfEmail.Items.Should().BeEmpty("the email matches by prefix only");
        wildcard.Items.Should().BeEmpty("LIKE wildcards in the term are escaped");
    }

    [Fact]
    public async Task ShopList_StatusFilterAndNewestFirstPaging()
    {
        var marker = Marker();
        var older = await fixture.CreateShopAsync($"أ {marker}");
        var newer = await fixture.CreateShopAsync($"ب {marker}");
        await fixture.ExecuteAsync("UPDATE shops SET created_at = created_at - interval '1 day' WHERE id = @p0", older.Id);
        await fixture.ExecuteAsync("UPDATE shops SET status = 2 WHERE id = @p0", newer.Id);
        var queries = ShopQueries();

        var all = await queries.ListAsync(marker, null, null, 1, 1, DayStart, PreferredLanguage.Ar, CancellationToken.None);
        var suspended = await queries.ListAsync(marker, null, ShopStatus.Suspended, 1, 20, DayStart, PreferredLanguage.Ar, CancellationToken.None);

        all.TotalCount.Should().Be(2);
        all.Items.Select(item => item.Id).Should().Equal(newer.Id);
        suspended.Items.Select(item => item.Id).Should().Equal(newer.Id);
        suspended.Items[0].OwnerEmail.Should().BeEmpty("a shop without an owner row still lists");
    }

    [Fact]
    public async Task ShopDetail_MembersCountsSubscriptionScansAndCost()
    {
        var (shop, owner) = await ShopWithOwnerAsync("صيدلية");
        var staff = await fixture.CreateUserAsync();
        await MemberAsync(shop.Id, staff, ShopRole.Staff);
        await SubscribeAsync(shop.Id, quotaOverride: 70);
        var invoice = await InvoiceAsync(shop.Id, InvoiceStatus.Failed, DayStart.AddHours(1));
        await UsageAsync(shop.Id, invoice, TestTime.T0.AddDays(-1), cost: 0.0015m);
        await UsageAsync(shop.Id, invoice, TestTime.T0.AddDays(-31), cost: 9m);
        await using (var writer = fixture.CreateContext(shop.Id))
        {
            writer.Products.Add(Catalog.NewProduct(shop.Id));
            await writer.SaveChangesAsync();
        }

        var detail = await ShopQueries().FindDetailAsync(shop.Id, DayStart, TestTime.T0.AddDays(-30), PreferredLanguage.En, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Members.Select(member => (member.UserId, member.Role)).Should().Equal((owner, ContractShopRole.Owner), (staff, ContractShopRole.Staff));
        detail.Counts.Should().Be(new Contracts.Admin.AdminShopCounts(1, 1, 0));
        detail.ScansToday.Should().Be(1);
        detail.AiCostLast30DaysUsd.Should().Be("0.00150000");
        detail.Subscription.Should().BeEquivalentTo(new
        {
            PlanCode = DefaultPlan.Code,
            PlanName = DefaultPlan.NameEn,
            PlanDailyScanQuota = DefaultPlan.DailyScanQuota,
            DailyScanQuotaOverride = 70,
            EffectiveDailyScanQuota = 70,
        });
        (await ShopQueries().FindDetailAsync(Guid.CreateVersion7(), DayStart, TestTime.T0, PreferredLanguage.Ar, CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task AddSubscription_AfterEndingTheCurrentOne_LeavesOneActiveCarryingTheOverride()
    {
        var shop = await fixture.CreateShopAsync();
        await SubscribeAsync(shop.Id, quotaOverride: 40);
        var plan = await PlanAsync();

        await using var scope = fixture.CreateScope(shopId: null);
        var shops = scope.ServiceProvider.GetRequiredService<IAdminShopRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var current = await shops.FindActiveSubscriptionAsync(shop.Id, token);
                current!.End(new DateOnly(2026, 10, 12));
                var next = ShopSubscription.Start(shop.Id, plan, new DateOnly(2026, 10, 12), null, current.DailyScanQuotaOverride, TestTime.T0).Value;
                await shops.AddSubscriptionAsync(next, token);
                await unitOfWork.SaveChangesAsync(token);
                return Result.Success();
            },
            _ => true,
            CancellationToken.None);

        (await fixture.ScalarAsync<long>("SELECT count(*) FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", shop.Id)).Should().Be(1);
        (await fixture.ScalarAsync<Guid>("SELECT plan_id FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", shop.Id)).Should().Be(plan);
        (await fixture.ScalarAsync<int>("SELECT daily_scan_quota_override FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", shop.Id))
            .Should().Be(40);
        var subscription = await ShopQueries().FindSubscriptionAsync(shop.Id, PreferredLanguage.Ar, CancellationToken.None);
        subscription!.EffectiveDailyScanQuota.Should().Be(40);
    }

    [Fact]
    public async Task AddSubscription_OutsideATransaction_Throws()
    {
        await using var scope = fixture.CreateScope(shopId: null);
        var shops = scope.ServiceProvider.GetRequiredService<IAdminShopRepository>();
        var subscription = ShopSubscription.StartOpenEnded(Guid.CreateVersion7(), DefaultPlan.Id, new DateOnly(2026, 10, 12), TestTime.T0).Value;

        await FluentActions.Invoking(() => shops.AddSubscriptionAsync(subscription, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task QuotaOverride_OutOfRange_IsRejectedByTheCheck()
    {
        var shop = await fixture.CreateShopAsync();
        await SubscribeAsync(shop.Id);

        var act = () => fixture.ExecuteAsync("UPDATE shop_subscriptions SET daily_scan_quota_override = 10001 WHERE shop_id = @p0", shop.Id);

        await act.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.ConstraintName == "ck_shop_subscriptions_daily_scan_quota_override");
    }

    [Fact]
    public async Task EffectiveQuotaForTheScan_OverrideWinsOverThePlan()
    {
        var shop = await fixture.CreateShopAsync();
        await SubscribeAsync(shop.Id);
        await using (var scope = fixture.CreateScope(shop.Id))
        {
            (await scope.ServiceProvider.GetRequiredService<IShopPlanQueries>().GetDailyScanQuotaAsync(CancellationToken.None))
                .Should().Be(DefaultPlan.DailyScanQuota);
        }

        await fixture.ExecuteAsync("UPDATE shop_subscriptions SET daily_scan_quota_override = 0 WHERE shop_id = @p0", shop.Id);

        await using (var scope = fixture.CreateScope(shop.Id))
        {
            (await scope.ServiceProvider.GetRequiredService<IShopPlanQueries>().GetDailyScanQuotaAsync(CancellationToken.None)).Should().Be(0);
        }
    }

    [Fact]
    public async Task RevokeMemberRefreshTokens_RevokesOnlyTheShopMembersActiveTokens()
    {
        var (shop, owner) = await ShopWithOwnerAsync("صيدلية");
        var outsider = await fixture.CreateUserAsync();
        var ownerToken = await TokenAsync(owner);
        var outsiderToken = await TokenAsync(outsider);

        await using var scope = fixture.CreateScope(shopId: null);
        var revoked = await scope.ServiceProvider.GetRequiredService<IAdminShopRepository>()
            .RevokeMemberRefreshTokensAsync(shop.Id, TestTime.T1, CancellationToken.None);

        revoked.Should().Be(1);
        (await fixture.ScalarAsync<DateTime?>("SELECT revoked_at FROM refresh_tokens WHERE id = @p0", ownerToken)).Should().NotBeNull();
        (await fixture.ScalarAsync<object>("SELECT revoked_at FROM refresh_tokens WHERE id = @p0", outsiderToken)).Should().Be(DBNull.Value);
    }

    [Fact]
    public async Task DailyUsage_GroupsByCairoDayWithCallsCacheHitsFailuresTokensAndCost()
    {
        // Winter: Cairo is UTC+2. 2031-01-14T21:59:59Z is still Jan 14 in Cairo; 22:00:00Z is Jan 15.
        var shop = await fixture.CreateShopAsync();
        var invoice = await InvoiceAsync(shop.Id, InvoiceStatus.NeedsReview, TestTime.T0);
        var lastSecond = new DateTimeOffset(2031, 1, 14, 21, 59, 59, TimeSpan.Zero);
        var midnight = new DateTimeOffset(2031, 1, 14, 22, 0, 0, TimeSpan.Zero);
        await UsageAsync(shop.Id, invoice, lastSecond, cost: 0.001m, prompt: 100, completion: 10);
        await UsageAsync(shop.Id, invoice, midnight, cost: 0.002m, prompt: 200, completion: 20);
        await UsageAsync(shop.Id, invoice, midnight.AddHours(1), cost: 0m, outcome: AiCallOutcome.ServerError);
        await UsageAsync(shop.Id, invoice, midnight.AddHours(2), cost: 0m, cacheHit: true);

        await using var scope = fixture.CreateScope(shopId: null);
        var usage = scope.ServiceProvider.GetRequiredService<IAdminAiUsageQueries>();
        var days = await usage.DailyAsync(
            new DateTimeOffset(2031, 1, 13, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2031, 1, 15, 22, 0, 0, TimeSpan.Zero), null, CancellationToken.None);
        var otherShop = await usage.DailyAsync(
            new DateTimeOffset(2031, 1, 13, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2031, 1, 15, 22, 0, 0, TimeSpan.Zero), Guid.CreateVersion7(), CancellationToken.None);

        days.Should().Equal(
            new Contracts.Admin.AiUsageDay(new DateOnly(2031, 1, 14), 1, 0, 0, 100, 10, "0.00100000"),
            new Contracts.Admin.AiUsageDay(new DateOnly(2031, 1, 15), 2, 1, 1, 200, 20, "0.00200000"));
        otherShop.Should().BeEmpty();
    }

    [Fact]
    public async Task UsageByShopAndByModel_HighestCostFirst()
    {
        var cheap = await fixture.CreateShopAsync("رخيص");
        var dear = await fixture.CreateShopAsync("غالي");
        var at = new DateTimeOffset(2032, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var cheapInvoice = await InvoiceAsync(cheap.Id, InvoiceStatus.NeedsReview, TestTime.T0);
        var dearInvoice = await InvoiceAsync(dear.Id, InvoiceStatus.NeedsReview, TestTime.T0);
        await UsageAsync(cheap.Id, cheapInvoice, at, cost: 0.001m, model: "model/b", latency: 1000);
        await UsageAsync(dear.Id, dearInvoice, at, cost: 0.004m, model: "model/a", latency: 2000);
        await UsageAsync(dear.Id, dearInvoice, at, cost: 0m, model: "model/a", latency: 4000, outcome: AiCallOutcome.Timeout);
        await UsageAsync(dear.Id, dearInvoice, at, cost: 0m, model: "model/a", latency: 5, cacheHit: true);

        await using var scope = fixture.CreateScope(shopId: null);
        var usage = scope.ServiceProvider.GetRequiredService<IAdminAiUsageQueries>();
        var from = at.AddHours(-1);
        var to = at.AddHours(1);
        var firstPage = await usage.ByShopAsync(from, to, 1, 1, CancellationToken.None);
        var secondPage = await usage.ByShopAsync(from, to, 2, 1, CancellationToken.None);
        var models = await usage.ByModelAsync(from, to, CancellationToken.None);

        firstPage.TotalCount.Should().Be(2);
        firstPage.Items.Should().Equal(new Contracts.Admin.AiUsageByShop(dear.Id, "غالي", 2, 1, "0.00400000"));
        secondPage.Items.Should().Equal(new Contracts.Admin.AiUsageByShop(cheap.Id, "رخيص", 1, 0, "0.00100000"));
        models.Should().Equal(
            new Contracts.Admin.AiUsageByModel("model/a", 2, 1, "0.00400000", 3000),
            new Contracts.Admin.AiUsageByModel("model/b", 1, 0, "0.00100000", 1000));
    }

    [Fact]
    public async Task FailedInvoices_EveryShopNewestFirstWithAttempts()
    {
        var shopA = await fixture.CreateShopAsync("أ");
        var shopB = await fixture.CreateShopAsync("ب");
        var older = await InvoiceAsync(shopA.Id, InvoiceStatus.Failed, new DateTimeOffset(2033, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = await InvoiceAsync(shopB.Id, InvoiceStatus.Failed, new DateTimeOffset(2033, 1, 2, 0, 0, 0, TimeSpan.Zero));
        await InvoiceAsync(shopB.Id, InvoiceStatus.NeedsReview, new DateTimeOffset(2033, 1, 3, 0, 0, 0, TimeSpan.Zero));
        await UsageAsync(shopB.Id, newer, TestTime.T0, cost: 0m, outcome: AiCallOutcome.ServerError);
        await UsageAsync(shopB.Id, newer, TestTime.T0, cost: 0m, outcome: AiCallOutcome.ServerError);
        await UsageAsync(shopB.Id, newer, TestTime.T0, cost: 0m, cacheHit: true);

        await using var scope = fixture.CreateScope(shopId: null);
        var invoices = scope.ServiceProvider.GetRequiredService<IAdminInvoiceQueries>();
        var all = await invoices.ListFailedAsync(null, 1, 100, CancellationToken.None);
        var ofB = await invoices.ListFailedAsync(shopB.Id, 1, 20, CancellationToken.None);

        all.Items.Select(item => item.InvoiceId).Should().ContainInOrder(newer, older);
        ofB.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            InvoiceId = newer,
            ShopId = shopB.Id,
            ShopName = "ب",
            FailureReason = "AiUnavailable",
            Attempts = 2,
        });
    }

    [Fact]
    public async Task InvoiceDetail_AnyShopWithRawJsonImageKeyAndUsageOldestFirst()
    {
        var shop = await fixture.CreateShopAsync("صيدلية");
        var invoice = await InvoiceAsync(shop.Id, InvoiceStatus.Failed, TestTime.T0);
        await fixture.ExecuteAsync("UPDATE invoices SET raw_ai_json = '{\"items\": []}'::jsonb WHERE id = @p0", invoice);
        await UsageAsync(shop.Id, invoice, TestTime.T1, cost: 0.002m, prompt: 50);
        await UsageAsync(shop.Id, invoice, TestTime.T0, cost: 0m, outcome: AiCallOutcome.RateLimited);

        await using var scope = fixture.CreateScope(shopId: null);
        var record = await scope.ServiceProvider.GetRequiredService<IAdminInvoiceQueries>().FindAsync(invoice, CancellationToken.None);

        record.Should().NotBeNull();
        record!.Invoice.Id.Should().Be(invoice);
        record.Invoice.FailureReason.Should().Be("AiUnavailable");
        record.ShopId.Should().Be(shop.Id);
        record.ShopName.Should().Be("صيدلية");
        record.ImageObjectKey.Should().Be($"invoices/{invoice}.jpg");
        JsonDocument.Parse(record.RawAiJson!).RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        record.Usage.Select(usage => (usage.Succeeded, usage.CostUsd)).Should().Equal((false, "0.00000000"), (true, "0.00200000"));
        (await scope.ServiceProvider.GetRequiredService<IAdminInvoiceQueries>().FindAsync(Guid.CreateVersion7(), CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task InvoiceForUpdate_AnyShopTrackedSoTheRerunSaves()
    {
        var shop = await fixture.CreateShopAsync();
        var invoice = await InvoiceAsync(shop.Id, InvoiceStatus.Failed, TestTime.T0);

        await using var scope = fixture.CreateScope(shopId: null);
        var queries = scope.ServiceProvider.GetRequiredService<IAdminInvoiceQueries>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var loaded = await queries.FindForUpdateAsync(invoice, token);
                loaded!.Rerun();
                await unitOfWork.SaveChangesAsync(token);
                return Result.Success();
            },
            _ => true,
            CancellationToken.None);

        (await fixture.ScalarAsync<short>("SELECT status FROM invoices WHERE id = @p0", invoice)).Should().Be((short)InvoiceStatus.Processing);
        await FluentActions.Invoking(() => queries.FindForUpdateAsync(invoice, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AuditLog_AddedThenListedWithFiltersEmailAndJson()
    {
        var admin = await fixture.CreateUserAsync();
        var target = Guid.CreateVersion7();
        await using (var writer = fixture.CreateScope(shopId: null))
        {
            var log = writer.ServiceProvider.GetRequiredService<IAdminAuditLog>();
            log.Add(AdminAuditLog.Create(admin, "Shop.Suspend", "Shop", target, "unpaid", """{"status":"Active"}""", """{"status":"Suspended"}""", TestTime.T0));
            log.Add(AdminAuditLog.Create(admin, "Shop.Activate", "Shop", target, "paid", null, null, TestTime.T1));
            log.Add(AdminAuditLog.Create(admin, "Plan.Create", "Plan", Guid.CreateVersion7(), null, null, "{}", TestTime.T2));
            await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var scope = fixture.CreateScope(shopId: null);
        var audit = scope.ServiceProvider.GetRequiredService<IAdminAuditLog>();
        var byTarget = await audit.ListAsync("Shop", target, null, 1, 20, CancellationToken.None);
        var byAdmin = await audit.ListAsync(null, null, admin, 1, 2, CancellationToken.None);

        byTarget.Items.Select(entry => entry.Action).Should().Equal("Shop.Activate", "Shop.Suspend");
        var suspend = byTarget.Items[1];
        suspend.AdminEmail.Should().Be(await EmailOfAsync(admin));
        suspend.Reason.Should().Be("unpaid");
        suspend.Before!.Value.GetProperty("status").GetString().Should().Be("Active");
        byTarget.Items[0].Before.Should().BeNull();
        byAdmin.TotalCount.Should().Be(3);
        byAdmin.Items.Select(entry => entry.Action).Should().Equal("Plan.Create", "Shop.Activate");
    }

    [Fact]
    public async Task Plans_ListActiveFirstThenByCode_FindTracked_CodeExists()
    {
        var code = $"z-{Guid.NewGuid():N}"[..20];
        await using (var writer = fixture.CreateScope(shopId: null))
        {
            writer.ServiceProvider.GetRequiredService<IAdminPlanRepository>().Add(Plan.Create(code, "ز", "Z", 1, 1, 0m, isActive: false).Value);
            await writer.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var scope = fixture.CreateScope(shopId: null);
        var plans = scope.ServiceProvider.GetRequiredService<IAdminPlanRepository>();
        var all = await plans.ListAsync(CancellationToken.None);

        all.Should().Contain(plan => plan.Code == DefaultPlan.Code);
        all.Select(plan => plan.IsActive).Should().BeInDescendingOrder();
        (await plans.CodeExistsAsync(code, CancellationToken.None)).Should().BeTrue();
        (await plans.CodeExistsAsync("no-such-code", CancellationToken.None)).Should().BeFalse();
        (await plans.FindAsync(DefaultPlan.Id, CancellationToken.None))!.Code.Should().Be(DefaultPlan.Code);
    }

    [Fact]
    public async Task PlanCode_TakenByAConcurrentCreate_IsCodeTaken()
    {
        await using var scope = fixture.CreateScope(shopId: null);
        scope.ServiceProvider.GetRequiredService<IAdminPlanRepository>().Add(Plan.Create(DefaultPlan.Code, "x", "x", 1, 1, 0m).Value);

        var saved = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().TrySaveChangesAsync(CancellationToken.None);

        saved.Error.Should().Be(PlanErrors.CodeTaken);
    }

    [Fact]
    public async Task Shop_NormalizedNameAndSuspension_RoundTrip()
    {
        var shop = Shop.Create("صَيدلية الشفاة", ShopType.Pharmacy, null, null, null, TestTime.T0).Value;
        shop.Suspend("unpaid", TestTime.T1);
        await using (var writer = fixture.CreateContext(shopId: null))
        {
            writer.Shops.Add(shop);
            await writer.SaveChangesAsync();
        }

        await using var reader = fixture.CreateContext(shopId: null);
        var loaded = await reader.Shops.AsNoTracking().SingleAsync(candidate => candidate.Id == shop.Id);

        loaded.NormalizedName.Should().Be("صيدليه الشفاه");
        loaded.SuspendedAt.Should().Be(TestTime.T1);
        loaded.SuspendedReason.Should().Be("unpaid");
    }

    [Fact]
    public async Task PlatformAdminAccounts_CreateOnceNeverPromoteAndRecognize()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        await using var scope = fixture.CreateScope(shopId: null, withIdentity: true);
        var admins = scope.ServiceProvider.GetRequiredService<IPlatformAdminAccounts>();
        var users = scope.ServiceProvider.GetRequiredService<IUserAccounts>();
        var shopUser = await users.CreateAsync(new NewUserAccount($"shop-{email}", "Shop User", null, PreferredLanguage.Ar, TestTime.T0), "Str0ngPass", CancellationToken.None);

        var created = await admins.CreateAsync(new NewUserAccount(email, "Admin", null, PreferredLanguage.Ar, TestTime.T0), "Str0ngPass", CancellationToken.None);
        var again = await admins.CreateAsync(new NewUserAccount($"shop-{email}", "Admin", null, PreferredLanguage.Ar, TestTime.T0), "Str0ngPass", CancellationToken.None);

        (await admins.IsPlatformAdminAsync(created.Value, CancellationToken.None)).Should().BeTrue();
        (await admins.IsPlatformAdminAsync(shopUser.Value, CancellationToken.None)).Should().BeFalse("a shop user is never promoted");
        again.Error.Should().Be(AuthErrors.EmailTaken);
        (await admins.AnyAsync(CancellationToken.None)).Should().BeTrue();
        (await fixture.ScalarAsync<short>("SELECT platform_role FROM users WHERE id = @p0", created.Value)).Should().Be((short)PlatformRole.PlatformAdmin);
    }

    private static string Marker() => Guid.NewGuid().ToString("N")[..10];

    private IAdminShopQueries ShopQueries() =>
        fixture.CreateScope(shopId: null).ServiceProvider.GetRequiredService<IAdminShopQueries>();

    private async Task<(Shop Shop, Guid Owner)> ShopWithOwnerAsync(string name)
    {
        var shop = await fixture.CreateShopAsync(name);
        var owner = await fixture.CreateUserAsync();
        await MemberAsync(shop.Id, owner, ShopRole.Owner);
        return (shop, owner);
    }

    private Task<int> MemberAsync(Guid shopId, Guid userId, ShopRole role) =>
        fixture.ExecuteAsync(
            "INSERT INTO shop_members (shop_id, user_id, role, is_active, joined_at) VALUES (@p0, @p1, @p2, true, @p3)",
            shopId,
            userId,
            (short)role,
            role == ShopRole.Owner ? TestTime.T0 : TestTime.T1);

    private Task<int> SubscribeAsync(Guid shopId, int? quotaOverride = null) =>
        fixture.ExecuteAsync(
            "INSERT INTO shop_subscriptions (id, shop_id, plan_id, status, starts_on, created_at, daily_scan_quota_override) " +
            "VALUES (@p0, @p1, @p2, 1, DATE '2026-10-01', now(), @p3)",
            Guid.CreateVersion7(),
            shopId,
            DefaultPlan.Id,
            quotaOverride is { } value ? value : DBNull.Value);

    private async Task<Guid> PlanAsync()
    {
        var id = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO plans (id, code, name_ar, name_en, daily_scan_quota, max_members, monthly_price, is_active) VALUES (@p0, @p1, 'برو', 'Pro', 100, 5, 99, true)",
            id,
            $"p-{id:N}"[..30]);
        return id;
    }

    private async Task<Guid> InvoiceAsync(Guid shopId, InvoiceStatus status, DateTimeOffset createdAt)
    {
        var id = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO invoices (id, shop_id, client_request_id, created_by_user_id, status, image_object_key, image_sha256, created_at, failure_reason) " +
            "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, repeat('a', 64), @p6, @p7)",
            id,
            shopId,
            Guid.CreateVersion7(),
            Catalog.UserId,
            (short)status,
            $"invoices/{id}.jpg",
            createdAt,
            status == InvoiceStatus.Failed ? nameof(InvoiceFailureReason.AiUnavailable) : DBNull.Value);
        return id;
    }

    private Task<int> UsageAsync(
        Guid shopId,
        Guid invoiceId,
        DateTimeOffset createdAt,
        decimal cost,
        int prompt = 0,
        int completion = 0,
        string model = "test/model",
        int latency = 100,
        AiCallOutcome outcome = AiCallOutcome.Succeeded,
        bool cacheHit = false) =>
        fixture.ExecuteAsync(
            "INSERT INTO ai_usage_records (id, shop_id, invoice_id, model, prompt_tokens, completion_tokens, cost_usd, latency_ms, outcome, cache_hit, created_at) " +
            "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10)",
            Guid.CreateVersion7(),
            shopId,
            invoiceId,
            model,
            prompt,
            completion,
            cost,
            latency,
            (short)outcome,
            cacheHit,
            createdAt);

    private async Task<Guid> TokenAsync(Guid userId)
    {
        var id = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO refresh_tokens (id, user_id, token_hash, family_id, expires_at, created_at) VALUES (@p0, @p1, @p2, @p0, @p3, @p4)",
            id,
            userId,
            Guid.NewGuid().ToString("N"),
            TestTime.T2.AddDays(30),
            TestTime.T0);
        return id;
    }

    private async Task<string> EmailOfAsync(Guid userId) =>
        (await fixture.ScalarAsync<string>("SELECT email FROM users WHERE id = @p0", userId))!;
}
