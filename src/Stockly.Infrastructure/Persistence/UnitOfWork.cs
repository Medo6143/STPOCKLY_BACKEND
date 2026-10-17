using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;
using Stockly.Domain.Sales;
using Stockly.Infrastructure.Persistence.Configurations;

namespace Stockly.Infrastructure.Persistence;

/// <summary>One transaction per command, run inside the Npgsql execution strategy (a retry repeats the whole unit).</summary>
internal sealed class UnitOfWork(StocklyDbContext db) : IUnitOfWork
{
    /// <summary>
    /// Unique indexes whose violation is an expected conflict: the handler checked first, but a concurrent request took
    /// the value between that check and this save (a barcode, a scan's or a sale's client request id, or an invoice line's
    /// receipt taken by a concurrent confirm, the last safety net of backend.md §11 step 10). Every other unique
    /// violation stays an unexpected error (500).
    /// </summary>
    private static readonly Dictionary<string, Error> ConflictByConstraint = new(StringComparer.Ordinal)
    {
        [ProductConfiguration.BarcodeIndex] = ProductErrors.DuplicateBarcode,
        [ProductUnitConfiguration.BarcodeIndex] = ProductUnitErrors.DuplicateBarcode,
        [InvoiceConfiguration.ClientRequestIndex] = InvoiceErrors.DuplicateClientRequest,
        [SaleConfiguration.ClientRequestIndex] = SaleErrors.DuplicateClientRequest,
        [StockMovementConfiguration.InvoiceItemReasonIndex] = InvoiceErrors.AlreadyConfirmed,
    };

    /// <summary>
    /// Restrict foreign keys whose violation is an expected conflict: a line or alias referenced the unit after P8 checked
    /// it was unused (the last safety net of <c>ProductUnit.InUse</c>). Every other foreign-key violation stays unexpected (500).
    /// </summary>
    private static readonly Dictionary<string, Error> ConflictByForeignKey = new(StringComparer.Ordinal)
    {
        [InvoiceItemConfiguration.ProductUnitForeignKey] = ProductUnitErrors.InUse,
        [SaleItemConfiguration.ProductUnitForeignKey] = ProductUnitErrors.InUse,
        [ProductAliasConfiguration.ProductUnitForeignKey] = ProductUnitErrors.InUse,
    };

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

    public async Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException exception) when (KnownConflict(exception) is not null)
        {
            return KnownConflict(exception)!;
        }
    }

    private static Error? KnownConflict(DbUpdateException exception) =>
        exception.InnerException switch
        {
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } constraint }
                when ConflictByConstraint.TryGetValue(constraint, out var error) => error,
            PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: { } constraint }
                when ConflictByForeignKey.TryGetValue(constraint, out var error) => error,
            _ => null,
        };
}
