using Stockly.Application.Abstractions.Time;

namespace Stockly.Infrastructure.Time;

/// <summary>The only place that reads the system clock (AGENTS.md rule 42).</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
