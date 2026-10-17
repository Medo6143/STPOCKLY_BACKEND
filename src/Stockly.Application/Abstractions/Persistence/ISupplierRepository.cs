using Stockly.Domain.Suppliers;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Supplier lookups for the current shop (tenant-filtered).</summary>
public interface ISupplierRepository
{
    /// <summary>The non-archived supplier with this Arabic-normalized name; <c>null</c> when none.</summary>
    Task<Guid?> FindIdByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken);

    /// <summary>
    /// The non-archived supplier with <paramref name="candidate"/>'s normalized name, inserting the candidate when there is
    /// none (<c>INSERT ... ON CONFLICT DO NOTHING</c>, then select; confirm, docs/design/backend.md §11 step 4). A
    /// concurrent insert of the same name waits for the other transaction and then finds its row.
    /// </summary>
    Task<SupplierResolution> FindOrCreateAsync(Supplier candidate, CancellationToken cancellationToken);
}

/// <summary>The supplier's id and whether this call inserted it.</summary>
public sealed record SupplierResolution(Guid SupplierId, bool Created);
