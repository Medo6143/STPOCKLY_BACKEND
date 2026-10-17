using Stockly.Domain.Platform;

namespace Stockly.Domain.Tests.Platform;

public sealed class AiUsageRecordTests
{
    [Fact]
    public void Create_ProviderValues_KeepsThemWithCostRoundedToEightDecimals()
    {
        var invoiceId = Guid.CreateVersion7();

        var record = AiUsageRecord.Create(
            TestData.ShopId, invoiceId, " google/gemini-2.5-flash ", 1200, 340, 0.000123456789m, 2100, AiCallOutcome.Succeeded, false, TestData.Now);

        record.ShopId.Should().Be(TestData.ShopId);
        record.InvoiceId.Should().Be(invoiceId);
        record.Model.Should().Be("google/gemini-2.5-flash");
        record.PromptTokens.Should().Be(1200);
        record.CompletionTokens.Should().Be(340);
        record.CostUsd.Should().Be(0.00012346m);
        record.LatencyMs.Should().Be(2100);
        record.Outcome.Should().Be(AiCallOutcome.Succeeded);
        record.CacheHit.Should().BeFalse();
        record.CreatedAt.Should().Be(TestData.Now);
    }

    [Fact]
    public void Create_NegativeCounters_BecomeZero()
    {
        var record = AiUsageRecord.Create(TestData.ShopId, null, "m", -1, -2, -0.5m, -3, AiCallOutcome.Timeout, false, TestData.Now);

        record.PromptTokens.Should().Be(0);
        record.CompletionTokens.Should().Be(0);
        record.CostUsd.Should().Be(0m);
        record.LatencyMs.Should().Be(0);
    }

    [Fact]
    public void Create_BlankModel_Throws()
    {
        var act = () => AiUsageRecord.Create(TestData.ShopId, null, " ", 0, 0, 0m, 0, AiCallOutcome.Succeeded, true, TestData.Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AiCallOutcome_StoredValues_NeverChange()
    {
        Enum.GetValues<AiCallOutcome>().Select(outcome => (int)outcome).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        Enum.GetNames<AiCallOutcome>().Should().Equal(
            "Succeeded", "InvalidOutput", "RateLimited", "ServerError", "ClientError", "Timeout", "NetworkError", "Cancelled");
    }
}
