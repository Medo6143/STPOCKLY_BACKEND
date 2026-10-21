using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Products;
using Stockly.Domain.Suppliers;
using Stockly.Domain.Text;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;

namespace Stockly.Application.Features.Invoices.ConfirmInvoice;

/// <summary>
/// I5, docs/design/backend.md §11 (rules 1, 3, 4, 5, 19, 23, 24): in one READ COMMITTED transaction, lock the invoice;
/// return a confirmed one unchanged; refuse a processing or failed one; require a product on every line; find or create
/// the supplier; lock the existing products in one ordered statement; create the new products (§10.5); receive every
/// line through <see cref="IStockService"/> in base units; confirm the invoice; save; learn the aliases. After the
/// commit, and only for a confirmation this request made: bump the catalog version, write the status cache and drop the
/// supplier list if a supplier was created. Redis failures only log (rule 25).
/// </summary>
public sealed class ConfirmInvoiceCommandHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IInvoiceRepository invoices,
    IInvoiceQueries queries,
    IProductRepository products,
    ISupplierRepository suppliers,
    IProductAliasRepository aliases,
    IStockService stock,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    IInvoiceStatusCache statusCache,
    IClock clock) : IRequestHandler<ConfirmInvoiceCommand, Result<InvoiceDetail>>
{
    public async Task<Result<InvoiceDetail>> Handle(ConfirmInvoiceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId || currentTenant.ShopId is not { } shopId)
        {
            return AuthErrors.Unauthenticated;
        }

        var outcome = await unitOfWork.ExecuteInTransactionAsync(
            token => ConfirmAsync(request.InvoiceId, shopId, userId, token),
            result => result.IsSuccess,
            cancellationToken);

        if (outcome.IsFailure)
        {
            if (outcome.Error != InvoiceErrors.AlreadyConfirmed)
            {
                return outcome.Error;
            }

            // Step 10: a concurrent confirm received this invoice first; the rollback left nothing of this attempt.
            var confirmed = await queries.FindDetailAsync(request.InvoiceId, cancellationToken);
            if (confirmed is null)
            {
                return InvoiceErrors.NotFound;
            }

            return confirmed;
        }

        var confirmation = outcome.Value;
        if (confirmation.Snapshot is { } snapshot)
        {
            await cache.IncrementVersionAsync(CacheKeys.CatalogVersion(shopId), AfterCommit.Token);
            await statusCache.SetAsync(shopId, snapshot, AfterCommit.Token);
            if (confirmation.SupplierCreated)
            {
                await cache.RemoveAsync(CacheKeys.SuppliersList(shopId), AfterCommit.Token);
            }
        }

        return confirmation.Detail;
    }

    private async Task<Result<Confirmation>> ConfirmAsync(Guid invoiceId, Guid shopId, Guid userId, CancellationToken cancellationToken)
    {
        // Step 1: the invoice first (rule 4). Another shop's invoice is invisible (rule 10).
        var invoice = await invoices.FindForUpdateAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        // Step 2: idempotent repeat; nothing is written.
        if (invoice.Status == DomainInvoiceStatus.Confirmed)
        {
            return new Confirmation(await DetailAsync(invoice.Id, cancellationToken), Snapshot: null, SupplierCreated: false);
        }

        if (invoice.Status != DomainInvoiceStatus.NeedsReview)
        {
            return InvoiceErrors.NotConfirmable;
        }

        // Step 3.
        if (invoice.HasUnresolvedLine)
        {
            return InvoiceErrors.LineProductRequired;
        }

        var now = clock.UtcNow;
        var supplier = await ResolveSupplierAsync(invoice, shopId, now, cancellationToken);
        if (supplier.IsFailure)
        {
            return supplier.Error;
        }

        var lines = invoice.Items.OrderBy(item => item.LineNo).ToList();

        // Step 5: existing products in one ordered statement; quantities come from these rows (rule 24).
        var existingIds = lines.Where(line => line.ProductId is not null).Select(line => line.ProductId!.Value).Distinct().ToList();
        var locked = await stock.LockProductsAsync(existingIds, cancellationToken);

        // Step 6 (§10.5).
        var created = NewProductsFromInvoice.Create(lines, shopId, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        foreach (var product in created.Value.Values)
        {
            products.Add(product);
        }

        // Step 7: one receipt per line, in line order, in base units (ADR-0003).
        var createdProductIdByLineId = new Dictionary<Guid, Guid>();
        var received = new List<(InvoiceItem Line, Guid ProductId, Guid UnitId)>(lines.Count);
        foreach (var line in lines)
        {
            var (product, unit, baseQuantity) = Target(line, locked, created.Value);
            if (line.NewProductName is not null)
            {
                createdProductIdByLineId[line.Id] = product.Id;
            }

            var movement = stock.ReceiveFromInvoice(product, baseQuantity, invoice.Id, line.Id, userId);
            if (movement.IsFailure)
            {
                return movement.Error;
            }

            received.Add((line, product.Id, unit.Id));
        }

        // Step 9.
        var confirmed = invoice.Confirm(userId, supplier.Value.SupplierId, createdProductIdByLineId, now);
        if (confirmed.IsFailure)
        {
            return confirmed.Error;
        }

        // Step 10. The aliases reference the new products, so they are written after the save, in the same transaction.
        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // Step 8.
        var learned = InvoiceAliasLearning.Learn(received, shopId, supplier.Value.SupplierId, now);
        if (learned.IsFailure)
        {
            return learned.Error;
        }

        await aliases.UpsertAsync(learned.Value, cancellationToken);
        return new Confirmation(
            await DetailAsync(invoice.Id, cancellationToken), invoice.ToStatusSnapshot(), supplier.Value.Created);
    }

    // Step 4: keep a known supplier; else find or create one from the name read on the invoice; else none.
    private async Task<Result<SupplierChoice>> ResolveSupplierAsync(
        Invoice invoice, Guid shopId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (invoice.SupplierId is { } known)
        {
            return new SupplierChoice(known, Created: false);
        }

        if (invoice.SupplierNameRaw is not { } name || ArabicTextNormalizer.Normalize(name).Length == 0)
        {
            return new SupplierChoice(SupplierId: null, Created: false);
        }

        var candidate = Supplier.Create(shopId, name, phone: null, now);
        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        var resolved = await suppliers.FindOrCreateAsync(candidate.Value, cancellationToken);
        return new SupplierChoice(resolved.SupplierId, resolved.Created);
    }

    // An existing line's product is always locked (FK) and its unit always one of the product's (I4 checked it; units in
    // use cannot be deleted, P8), so a miss is a broken invariant, not a client error.
    private static (Product Product, ProductUnit Unit, decimal BaseQuantity) Target(
        InvoiceItem line, IReadOnlyDictionary<Guid, Product> locked, IReadOnlyDictionary<(string Name, string BaseUnit), Product> created)
    {
        if (line.ProductId is { } productId)
        {
            var product = locked.TryGetValue(productId, out var found)
                ? found
                : throw new InvalidOperationException("A confirmed line's product was not found under the lock.");
            var unit = product.FindUnit(line.ProductUnitId);
            return unit.IsSuccess
                ? (product, unit.Value, unit.Value.ToBase(line.Quantity))
                : throw new InvalidOperationException("A confirmed line's unit is not a unit of its product.");
        }

        // A new product's line quantity is already in its base unit (contract I4).
        var newProduct = created[NewProductsFromInvoice.KeyOf(line)];
        return (newProduct, newProduct.BaseUnit, line.Quantity);
    }

    private async Task<InvoiceDetail> DetailAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await queries.FindDetailAsync(invoiceId, cancellationToken)
        ?? throw new InvalidOperationException("The invoice locked in this transaction was not found.");

    /// <summary>The invoice's supplier (<c>null</c> when unknown) and whether this request created it.</summary>
    private sealed record SupplierChoice(Guid? SupplierId, bool Created);

    /// <summary>The confirmed invoice; <see cref="Snapshot"/> is set only when this request confirmed it.</summary>
    private sealed record Confirmation(InvoiceDetail Detail, InvoiceStatusSnapshot? Snapshot, bool SupplierCreated);
}
