using Stockly.Domain.Platform;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Append-only AI usage rows (AGENTS.md rule 18). Saved with the unit of work.</summary>
public interface IAiUsageRecordRepository
{
    void Add(AiUsageRecord record);
}
