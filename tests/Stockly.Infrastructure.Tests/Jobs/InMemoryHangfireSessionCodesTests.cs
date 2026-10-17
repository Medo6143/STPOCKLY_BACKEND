using Stockly.Infrastructure.Jobs;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Jobs;

/// <summary>The Hangfire dashboard's one-time codes (contract §11.3): single use, valid 60 seconds.</summary>
public sealed class InMemoryHangfireSessionCodesTests
{
    private readonly FakeClock _clock = new(TestTime.T0);

    [Fact]
    public void Redeem_FreshCode_ReturnsTheAdminOnce()
    {
        var codes = new InMemoryHangfireSessionCodes(_clock);
        var admin = Guid.CreateVersion7();
        var code = codes.Issue(admin);

        codes.Redeem(code).Should().Be(admin);
        codes.Redeem(code).Should().BeNull("a code is single use");
    }

    [Fact]
    public void Redeem_After60Seconds_IsNull()
    {
        var codes = new InMemoryHangfireSessionCodes(_clock);
        var code = codes.Issue(Guid.CreateVersion7());

        _clock.UtcNow = TestTime.T0.AddSeconds(60);

        codes.Redeem(code).Should().BeNull();
    }

    [Fact]
    public void Redeem_Within60Seconds_Works()
    {
        var codes = new InMemoryHangfireSessionCodes(_clock);
        var admin = Guid.CreateVersion7();
        var code = codes.Issue(admin);

        _clock.UtcNow = TestTime.T0.AddSeconds(59);

        codes.Redeem(code).Should().Be(admin);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-code")]
    public void Redeem_UnknownCode_IsNull(string code)
    {
        var codes = new InMemoryHangfireSessionCodes(_clock);
        codes.Issue(Guid.CreateVersion7());

        codes.Redeem(code).Should().BeNull();
    }

    [Fact]
    public void Issue_CodesAreRandomAndUrlSafe()
    {
        var codes = new InMemoryHangfireSessionCodes(_clock);

        var first = codes.Issue(Guid.CreateVersion7());
        var second = codes.Issue(Guid.CreateVersion7());

        first.Should().NotBe(second);
        first.Should().HaveLength(43).And.MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Issue_EmptyAdmin_Throws()
    {
        FluentActions.Invoking(() => new InMemoryHangfireSessionCodes(_clock).Issue(Guid.Empty)).Should().Throw<ArgumentException>();
    }
}
