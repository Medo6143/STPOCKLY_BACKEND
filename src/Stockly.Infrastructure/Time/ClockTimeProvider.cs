using Stockly.Application.Abstractions.Time;

namespace Stockly.Infrastructure.Time;

/// <summary>
/// <see cref="TimeProvider"/> for framework code (Identity lockout) that reads the time from <see cref="IClock"/>, so the
/// whole app follows one clock and tests can move it (AGENTS.md rule 42). Timers and timestamps stay the system's.
/// </summary>
public sealed class ClockTimeProvider(IClock clock) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => clock.UtcNow;
}
