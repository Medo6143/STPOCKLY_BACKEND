using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;

namespace Stockly.Infrastructure.Persistence;

/// <summary>One transaction per command, run inside the Npgsql execution strategy (a retry repeats the whole unit).</summary>
internal sealed class UnitOfWork(StocklyDbContext db) : IUnitOfWork
{
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, Func<TResult, bool> shouldCommit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(shouldCommit);

        return db.Database.CreateExecutionStrategy().ExecuteAsync(
            async token =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var result = await operation(token);
                if (shouldCommit(result))
                {
                    await transaction.CommitAsync(token);
                }
                else
                {
                    await transaction.RollbackAsync(token);
                }

                return result;
            },
            cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
