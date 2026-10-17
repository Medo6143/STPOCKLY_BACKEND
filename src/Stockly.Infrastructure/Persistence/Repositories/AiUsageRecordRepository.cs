using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Platform;

namespace Stockly.Infrastructure.Persistence.Repositories;

internal sealed class AiUsageRecordRepository(StocklyDbContext db) : IAiUsageRecordRepository
{
    public void Add(AiUsageRecord record) => db.Set<AiUsageRecord>().Add(record);
}
