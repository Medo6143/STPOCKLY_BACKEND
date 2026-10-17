using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Invoices;

namespace Stockly.Application.Tests.Features.Invoices;

/// <summary>Raw AI JSON storage, image keys, cache keys and the Cairo day used by the daily quota.</summary>
public sealed class InvoiceSupportTests
{
    private static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");

    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("[1,2]", "[1,2]")]
    [InlineData("not json", "\"not json\"")]
    [InlineData("cut off {", "\"cut off {\"")]
    [InlineData("{\"a\":\"\\u0000\"}", "\"{\\u0022a\\u0022:\\u0022\\\\u0000\\u0022}\"")]
    public void RawAiJson_From_KeepsJsonOrStoresAStringLiteral(string content, string expected)
    {
        RawAiJson.From(content).Should().Be(expected);
    }

    [Fact]
    public void RawAiJson_From_Null_IsNull()
    {
        RawAiJson.From(null).Should().BeNull();
    }

    [Fact]
    public void RawAiJson_From_TooLong_IsCutToTheLimit()
    {
        var content = new string('x', RawAiJson.MaxLength + 10);

        var stored = RawAiJson.From(content)!;

        System.Text.Json.JsonSerializer.Deserialize<string>(stored).Should().HaveLength(RawAiJson.MaxLength);
    }

    [Fact]
    public void ImageKey_UsesShopUtcYearMonthAndInvoiceId()
    {
        var invoiceId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000e1");

        InvoiceImageKeys.For(ShopId, invoiceId, new DateTimeOffset(2026, 1, 31, 23, 30, 0, TimeSpan.FromHours(-2)))
            .Should().Be($"invoices/{ShopId}/2026/02/{invoiceId}.jpg");
    }

    [Fact]
    public void CacheKeys_InvoiceKeys_StartWithTheTenantPrefix()
    {
        var at = new DateTimeOffset(2026, 10, 12, 7, 5, 59, TimeSpan.Zero);

        CacheKeys.AiInvoice(ShopId, "ab").Should().Be($"stockly:{ShopId}:ai:invoice:ab");
        CacheKeys.InvoiceStatus(ShopId, ShopId).Should().Be($"stockly:{ShopId}:invoice:{ShopId}:status");
        CacheKeys.ScanRate(ShopId, at).Should().Be($"stockly:{ShopId}:rl:scan:202610120705");
        CacheKeys.ScanQuota(ShopId, new DateOnly(2026, 10, 12)).Should().Be($"stockly:{ShopId}:quota:scan:20261012");
    }

    [Theory]
    [InlineData("2026-10-12T21:30:00Z", "2026-10-12T21:00:00Z", 84600)]
    [InlineData("2026-12-15T10:00:00Z", "2026-12-14T22:00:00Z", 43200)]
    public void BusinessCalendar_CairoDay_StartAndTimeToNextMidnight(string now, string dayStart, int secondsToMidnight)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        clock.CairoDayStartUtc().Should().Be(DateTimeOffset.Parse(dayStart, System.Globalization.CultureInfo.InvariantCulture));
        clock.UntilNextCairoMidnight().Should().Be(TimeSpan.FromSeconds(secondsToMidnight));
    }
}
