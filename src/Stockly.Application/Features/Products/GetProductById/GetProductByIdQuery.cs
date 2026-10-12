using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Products;

namespace Stockly.Application.Features.Products.GetProductById;

/// <summary>P2: the product with its units. Another shop's product is <c>Product.NotFound</c> (AGENTS.md rule 10).</summary>
public sealed record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDetail>;
