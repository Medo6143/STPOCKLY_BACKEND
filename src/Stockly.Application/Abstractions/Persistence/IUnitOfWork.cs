using Stockly.Domain.Common;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>The database transaction of one use case. Infrastructure runs it inside the provider's execution strategy.</summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> in one transaction. Commits when <paramref name="shouldCommit"/> returns
    /// <c>true</c> for its result, otherwise rolls back. An exception always rolls back.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, Func<TResult, bool> shouldCommit, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves like <see cref="SaveChangesAsync"/>, but a unique-index violation that is an expected conflict (a barcode
    /// taken by a concurrent request after the handler's check) comes back as its contract error, e.g.
    /// <c>Product.DuplicateBarcode</c>. Any other database failure still throws. The caller returns the failure, so the
    /// transaction rolls back.
    /// </summary>
    Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken);
}
