namespace Stockly.Application.Abstractions.Time;

/// <summary>The only source of the current time (AGENTS.md rule 42).</summary>
public interface IClock
{
    /// <summary>Current time in UTC (offset zero). Npgsql rejects non-UTC values for timestamptz.</summary>
    DateTimeOffset UtcNow { get; }
}
