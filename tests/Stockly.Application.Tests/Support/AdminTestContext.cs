using NSubstitute;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Jobs;
using Stockly.Application.Abstractions.Limits;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Storage;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Admin;
using Stockly.Contracts.Admin;
using Stockly.Domain.Platform;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;

namespace Stockly.Application.Tests.Support;

/// <summary>Substitutes for every port the admin handlers use, a fixed clock (Cairo winter: UTC+2) and a signed-in admin.</summary>
internal sealed class AdminTestContext
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Today = new(2026, 1, 15);
    public static readonly Guid AdminId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000ad");
    public static readonly UserAccount AdminAccount = new(AdminId, "admin@example.com", "Platform Admin", null, PreferredLanguage.Ar);

    private int _refreshCounter;

    public AdminTestContext()
    {
        Clock.UtcNow.Returns(_ => Now);
        CurrentUser.UserId.Returns(AdminId);
        Tokens.CreateAdminAccessToken(Arg.Any<Guid>()).Returns(new AccessToken("admin-access", Now + TokenLifetimes.AccessToken));
        Tokens.CreateRefreshToken().Returns(_ =>
        {
            var number = Interlocked.Increment(ref _refreshCounter);
            return new RefreshTokenValue($"refresh-{number}", $"hash-{number}");
        });
        Tokens.HashRefreshToken(Arg.Any<string>()).Returns(call => "hash-of-" + call.Arg<string>());
        AuditLog.When(log => log.Add(Arg.Any<AdminAuditLog>())).Do(call => Audited.Add(call.Arg<AdminAuditLog>()));
        Sessions = new AdminSessions(Tokens, RefreshTokens, Clock);
        Audit = new AdminAudit(AuditLog, Clock);
    }

    public IClock Clock { get; } = Substitute.For<IClock>();

    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();

    public ITokenService Tokens { get; } = Substitute.For<ITokenService>();

    public IUserAccounts Users { get; } = Substitute.For<IUserAccounts>();

    public IPlatformAdminAccounts Admins { get; } = Substitute.For<IPlatformAdminAccounts>();

    public IRefreshTokenRepository RefreshTokens { get; } = Substitute.For<IRefreshTokenRepository>();

    public IAdminShopRepository Shops { get; } = Substitute.For<IAdminShopRepository>();

    public IAdminShopQueries ShopQueries { get; } = Substitute.For<IAdminShopQueries>();

    public IAdminPlanRepository Plans { get; } = Substitute.For<IAdminPlanRepository>();

    public IAdminAiUsageQueries Usage { get; } = Substitute.For<IAdminAiUsageQueries>();

    public IAdminInvoiceQueries Invoices { get; } = Substitute.For<IAdminInvoiceQueries>();

    public IAdminAuditLog AuditLog { get; } = Substitute.For<IAdminAuditLog>();

    public IScanQuotaCounter Quota { get; } = Substitute.For<IScanQuotaCounter>();

    public IFileStorage Storage { get; } = Substitute.For<IFileStorage>();

    public IBackgroundJobScheduler Jobs { get; } = Substitute.For<IBackgroundJobScheduler>();

    public IInvoiceStatusCache StatusCache { get; } = Substitute.For<IInvoiceStatusCache>();

    public IHangfireSessionCodes Codes { get; } = Substitute.For<IHangfireSessionCodes>();

    public InlineUnitOfWork UnitOfWork { get; } = new();

    public AdminSessions Sessions { get; }

    public AdminAudit Audit { get; }

    public List<AdminAuditLog> Audited { get; } = [];

    public static Shop NewShop() => Shop.Create("صيدلية الشفاء", ShopType.Pharmacy, null, null, null, Now.AddDays(-30)).Value;

    public static ShopSubscription NewSubscription(Guid shopId, int? quotaOverride = null)
    {
        var subscription = ShopSubscription.StartOpenEnded(shopId, DefaultPlan.Id, Today.AddDays(-30), Now.AddDays(-30)).Value;
        subscription.SetQuotaOverride(quotaOverride);
        return subscription;
    }

    public static AdminSubscriptionDto SubscriptionDto(Guid planId, int? quotaOverride = null) =>
        new(planId, "standard", "الأساسية", Today, null, 30, quotaOverride, quotaOverride ?? 30);
}
