using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Messaging;

namespace Stockly.Application.Features.Products.ArchiveProduct;

/// <summary>P5 (Owner only, enforced by the Api policy): soft-deletes the product. Idempotent.</summary>
public sealed record ArchiveProductCommand(Guid ProductId) : ICommand, IInvalidatesCatalog;
