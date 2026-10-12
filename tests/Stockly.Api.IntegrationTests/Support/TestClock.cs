using Stockly.Application.Abstractions.Time;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
