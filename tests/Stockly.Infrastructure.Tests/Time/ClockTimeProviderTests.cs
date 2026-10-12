using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Time;
using Stockly.Infrastructure.Tests.Support;
using Stockly.Infrastructure.Time;

namespace Stockly.Infrastructure.Tests.Time;

public sealed class ClockTimeProviderTests
{
    [Fact]
    public void GetUtcNow_ReadsTheClock_AndFollowsItWhenItMoves()
    {
        var clock = new FakeClock(TestTime.T0);
        var provider = new ClockTimeProvider(clock);

        provider.GetUtcNow().Should().Be(TestTime.T0);
        clock.UtcNow = TestTime.T1;
        provider.GetUtcNow().Should().Be(TestTime.T1);
    }

    [Fact]
    public void AddPersistence_RegistersTimeProviderBackedByTheRegisteredClock()
    {
        var clock = new FakeClock(TestTime.T2);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICurrentTenant, NoTenant>();
        services.AddPersistence("Host=not-used;Database=not-used");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().GetUtcNow().Should().Be(TestTime.T2);
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? ShopId => null;
    }
}
