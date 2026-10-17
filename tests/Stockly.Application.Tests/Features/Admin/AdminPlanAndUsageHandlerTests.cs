using System.Globalization;
using System.Text.Json;
using NSubstitute;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Admin.CreatePlan;
using Stockly.Application.Features.Admin.GetAiUsageByModel;
using Stockly.Application.Features.Admin.GetAiUsageByShop;
using Stockly.Application.Features.Admin.GetDailyAiUsage;
using Stockly.Application.Features.Admin.GetPlans;
using Stockly.Application.Features.Admin.UpdatePlan;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;
using static Stockly.Application.Tests.Support.AdminTestContext;

namespace Stockly.Application.Tests.Features.Admin;

/// <summary>AD11–AD16.</summary>
public sealed class AdminPlanAndUsageHandlerTests
{
    private readonly AdminTestContext _context = new();

    [Fact]
    public async Task GetPlans_NamesInTheRequestLanguage()
    {
        var plan = Plan.Create("standard", "الأساسية", "Standard", 30, 3, 0m).Value;
        _context.Plans.ListAsync(Arg.Any<CancellationToken>()).Returns([plan]);
        var handler = new GetPlansQueryHandler(_context.Plans);

        var arabic = await InCulture("ar", () => handler.Handle(new GetPlansQuery(), CancellationToken.None));
        var english = await InCulture("en", () => handler.Handle(new GetPlansQuery(), CancellationToken.None));

        arabic.Value.Should().ContainSingle().Which.Should().Be(new PlanDto(plan.Id, "standard", "الأساسية", 30, 3, "0.00", true));
        english.Value.Single().Name.Should().Be("Standard");
    }

    [Fact]
    public async Task CreatePlan_NewCode_AddsWithTheNameInBothLanguagesAndAudits()
    {
        Plan? added = null;
        _context.Plans.Add(Arg.Do<Plan>(plan => added = plan));

        var result = await CreatePlan("pro", "Pro", "199.50");

        result.Value.Should().BeEquivalentTo(new { Code = "pro", Name = "Pro", DailyScanQuota = 100, MaxMembers = 5, MonthlyPrice = "199.50", IsActive = true });
        added!.NameAr.Should().Be("Pro");
        added.NameEn.Should().Be("Pro");
        var entry = _context.Audited.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AdminActions.PlanCreate);
        entry.Before.Should().BeNull();
        JsonDocument.Parse(entry.After!).RootElement.GetProperty("monthlyPrice").GetString().Should().Be("199.50");
        _context.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task CreatePlan_TakenCode_IsCodeTaken()
    {
        _context.Plans.CodeExistsAsync("pro", Arg.Any<CancellationToken>()).Returns(true);

        (await CreatePlan("pro", "Pro", "1.00")).Error.Should().Be(PlanErrors.CodeTaken);
        _context.Plans.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task CreatePlan_CodeTakenByAConcurrentCreate_IsCodeTaken()
    {
        _context.UnitOfWork.TrySaveResult = PlanErrors.CodeTaken;

        (await CreatePlan("pro", "Pro", "1.00")).Error.Should().Be(PlanErrors.CodeTaken);
    }

    [Fact]
    public async Task CreatePlan_ThreeDecimalPrice_IsTooManyDecimals()
    {
        (await CreatePlan("pro", "Pro", "1.001")).Error.Code.Should().Be("Precision.TooManyDecimals");
    }

    [Theory]
    [InlineData("pro", "Pro", 100, 5, "1.00", true)]
    [InlineData("Pro", "Pro", 100, 5, "1.00", false)]
    [InlineData("p", "Pro", 100, 5, "1.00", false)]
    [InlineData("pro plan", "Pro", 100, 5, "1.00", false)]
    [InlineData("pro", "", 100, 5, "1.00", false)]
    [InlineData("pro", "Pro", -1, 5, "1.00", false)]
    [InlineData("pro", "Pro", 100, 0, "1.00", false)]
    [InlineData("pro", "Pro", 100, 5, "abc", false)]
    public void CreatePlanValidator_Shape(string code, string name, int quota, int members, string price, bool valid)
    {
        new CreatePlanCommandValidator().Validate(new CreatePlanCommand(code, name, quota, members, price, true)).IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task UpdatePlan_Known_UpdatesAndAuditsBeforeAndAfter()
    {
        var plan = Plan.Create("pro", "برو", "Pro", 100, 5, 199m).Value;
        _context.Plans.FindAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);

        var result = await new UpdatePlanCommandHandler(_context.CurrentUser, _context.Plans, _context.Audit, _context.UnitOfWork)
            .Handle(new UpdatePlanCommand(plan.Id, "Pro+", 150, 6, "249.00", false), CancellationToken.None);

        result.Value.Should().Be(new PlanDto(plan.Id, "pro", "Pro+", 150, 6, "249.00", false));
        var entry = _context.Audited.Should().ContainSingle().Subject;
        JsonDocument.Parse(entry.Before!).RootElement.GetProperty("nameAr").GetString().Should().Be("برو");
        JsonDocument.Parse(entry.After!).RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task UpdatePlan_Unknown_IsPlanNotFound()
    {
        var result = await new UpdatePlanCommandHandler(_context.CurrentUser, _context.Plans, _context.Audit, _context.UnitOfWork)
            .Handle(new UpdatePlanCommand(Guid.CreateVersion7(), "x", 1, 1, "1.00", true), CancellationToken.None);

        result.Error.Should().Be(PlanErrors.NotFound);
        _context.Audited.Should().BeEmpty();
    }

    [Fact]
    public void UpdatePlanValidator_Shape()
    {
        var validator = new UpdatePlanCommandValidator();

        validator.Validate(new UpdatePlanCommand(Guid.CreateVersion7(), "Pro", 1, 1, "1.00", true)).IsValid.Should().BeTrue();
        validator.Validate(new UpdatePlanCommand(Guid.CreateVersion7(), "", 10_001, 0, "x", true)).Errors.Should().HaveCount(4);
    }

    [Fact]
    public async Task Daily_DefaultRange_IsTheLast30CairoDaysZeroFilledOldestFirst()
    {
        // Now is 2026-01-15T10:00Z, Cairo UTC+2 in January: the range is 2025-12-17 .. 2026-01-15 (Cairo).
        var from = new DateTimeOffset(2025, 12, 16, 22, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 15, 22, 0, 0, TimeSpan.Zero);
        var busy = new AiUsageDay(Today.AddDays(-1), 3, 1, 1, 900, 300, "0.00120000");
        _context.Usage.DailyAsync(from, to, null, Arg.Any<CancellationToken>()).Returns([busy]);

        var result = await new GetDailyAiUsageQueryHandler(_context.Usage, _context.Clock)
            .Handle(new GetDailyAiUsageQuery(null, null, null), CancellationToken.None);

        var days = result.Value;
        days.Should().HaveCount(30);
        days[0].Date.Should().Be(new DateOnly(2025, 12, 17));
        days[^1].Should().Be(new AiUsageDay(Today, 0, 0, 0, 0, 0, "0.00000000"));
        days[^2].Should().Be(busy);
        days.Select(day => day.Date).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Daily_ExplicitRangeAndShop_PassesCairoBoundaries()
    {
        var shop = Guid.CreateVersion7();
        var day = new DateOnly(2026, 7, 1); // Cairo summer time (UTC+3)

        var result = await new GetDailyAiUsageQueryHandler(_context.Usage, _context.Clock)
            .Handle(new GetDailyAiUsageQuery(day, day, shop), CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Date.Should().Be(day);
        await _context.Usage.Received(1).DailyAsync(
            new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 21, 0, 0, TimeSpan.Zero),
            shop,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("2026-01-01", "2026-04-03", true)]
    [InlineData("2026-01-01", "2026-04-04", false)]
    [InlineData("2026-01-02", "2026-01-01", false)]
    [InlineData(null, "2025-06-01", true)]
    [InlineData("2025-01-01", null, false)]
    public void UsageValidators_RangeOrderedAndAtMost92Days(string? from, string? to, bool valid)
    {
        var fromDate = from is null ? (DateOnly?)null : DateOnly.Parse(from, CultureInfo.InvariantCulture);
        var toDate = to is null ? (DateOnly?)null : DateOnly.Parse(to, CultureInfo.InvariantCulture);

        new GetDailyAiUsageQueryValidator(_context.Clock).Validate(new GetDailyAiUsageQuery(fromDate, toDate, null)).IsValid.Should().Be(valid);
        new GetAiUsageByShopQueryValidator(_context.Clock).Validate(new GetAiUsageByShopQuery(fromDate, toDate, 1, 20)).IsValid.Should().Be(valid);
        new GetAiUsageByModelQueryValidator(_context.Clock).Validate(new GetAiUsageByModelQuery(fromDate, toDate)).IsValid.Should().Be(valid);
    }

    [Fact]
    public void UsageValidator_ErrorIsOnTo()
    {
        var result = new GetDailyAiUsageQueryValidator(_context.Clock)
            .Validate(new GetDailyAiUsageQuery(new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1), null));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("to");
    }

    [Fact]
    public async Task ByShopAndByModel_PassTheResolvedRange()
    {
        var page = new PagedResponse<AiUsageByShop>([], 1, 20, 0);
        var from = new DateTimeOffset(2026, 1, 9, 22, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 10, 22, 0, 0, TimeSpan.Zero);
        _context.Usage.ByShopAsync(from, to, 1, 20, Arg.Any<CancellationToken>()).Returns(page);
        _context.Usage.ByModelAsync(from, to, Arg.Any<CancellationToken>()).Returns([new AiUsageByModel("m", 1, 0, "0.10000000", 900)]);
        var day = new DateOnly(2026, 1, 10);

        var byShop = await new GetAiUsageByShopQueryHandler(_context.Usage, _context.Clock)
            .Handle(new GetAiUsageByShopQuery(day, day, 1, 20), CancellationToken.None);
        var byModel = await new GetAiUsageByModelQueryHandler(_context.Usage, _context.Clock)
            .Handle(new GetAiUsageByModelQuery(day, day), CancellationToken.None);

        byShop.Value.Should().BeSameAs(page);
        byModel.Value.Should().ContainSingle().Which.Model.Should().Be("m");
    }

    [Fact]
    public void Usd_HasEightDecimals()
    {
        AdminMapping.Usd(0.0012m).Should().Be("0.00120000");
        AdminMapping.Usd(0m).Should().Be("0.00000000");
    }

    private Task<Result<PlanDto>> CreatePlan(string code, string name, string price) =>
        new CreatePlanCommandHandler(_context.CurrentUser, _context.Plans, _context.Audit, _context.UnitOfWork)
            .Handle(new CreatePlanCommand(code, name, 100, 5, price, true), CancellationToken.None);

    private static async Task<T> InCulture<T>(string name, Func<Task<T>> run)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(name);
        try
        {
            return await run();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
