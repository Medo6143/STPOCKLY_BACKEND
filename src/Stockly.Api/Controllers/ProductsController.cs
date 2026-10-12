using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Authentication;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Products.AddProductUnit;
using Stockly.Application.Features.Products.AdjustStock;
using Stockly.Application.Features.Products.ArchiveProduct;
using Stockly.Application.Features.Products.CountStock;
using Stockly.Application.Features.Products.CreateProduct;
using Stockly.Application.Features.Products.GetLowStock;
using Stockly.Application.Features.Products.GetProductById;
using Stockly.Application.Features.Products.GetProductMovements;
using Stockly.Application.Features.Products.GetProducts;
using Stockly.Application.Features.Products.RemoveProductUnit;
using Stockly.Application.Features.Products.UpdateProduct;
using Stockly.Application.Features.Products.UpdateProductUnit;
using Stockly.Application.Validation;
using Stockly.Contracts.Common;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;

namespace Stockly.Api.Controllers;

/// <summary>
/// Contract rows P1–P11. Maps HTTP to MediatR and back; no business decisions here (AGENTS.md rule 26). Owner-only
/// rows use the <see cref="AuthPolicies.Owner"/> policy; the shop always comes from the token (ADR-0002).
/// </summary>
[ApiController]
[Route("api/products")]
[Authorize]
public sealed class ProductsController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>P1: a page of products sorted by normalized name. <c>lowStock=true</c> is served by <c>GetLowStockQuery</c>.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<ProductListItem>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? search = null,
        [FromQuery] bool lowStock = false,
        [FromQuery] bool includeArchived = false,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        IRequest<Result<PagedResponse<ProductListItem>>> query = lowStock
            ? new GetLowStockQuery(search, includeArchived, page, pageSize)
            : new GetProductsQuery(search, includeArchived, page, pageSize);
        var result = await sender.Send(query, cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>P2: the product with its units.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductDetail>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProductByIdQuery(id), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>P3: create a product with its base unit; <c>openingQuantity</c> is Owner only.</summary>
    [HttpPost]
    [ProducesResponseType<ProductDetail>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new CreateProductCommand(
            request.Name,
            request.BaseUnitName,
            request.CostPrice,
            request.BaseUnitSellPrice,
            request.Barcode,
            request.Category,
            request.LowStockThreshold,
            request.OpeningQuantity);
        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult(this, product => Created(ProductLocation(product.Id), product));
    }

    /// <summary>P4: replace the product's details.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProductDetail>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new UpdateProductCommand(
            id, request.Name, request.CostPrice, request.LowStockThreshold, request.Barcode, request.Category);
        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>P5: archive (soft delete). Idempotent. Owner only.</summary>
    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = AuthPolicies.Owner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ArchiveProductCommand(id), cancellationToken);
        return result.ToActionResult(this, NoContent);
    }

    /// <summary>P6: add a unit.</summary>
    [HttpPost("{id:guid}/units")]
    [ProducesResponseType<UnitDto>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> AddUnitAsync(Guid id, ProductUnitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new AddProductUnitCommand(id, request.Name, request.Factor, request.SellPrice, request.Barcode);
        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult(this, unit => Created(UnitLocation(id, unit.Id), unit));
    }

    /// <summary>P7: replace a unit. The base unit's factor stays 1.</summary>
    [HttpPut("{id:guid}/units/{unitId:guid}")]
    [ProducesResponseType<UnitDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> UpdateUnitAsync(Guid id, Guid unitId, ProductUnitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new UpdateProductUnitCommand(id, unitId, request.Name, request.Factor, request.SellPrice, request.Barcode);
        var result = await sender.Send(command, cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>P8: delete a unit (never the base unit).</summary>
    [HttpDelete("{id:guid}/units/{unitId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, StocklyProblems.ContentType)]
    public async Task<IActionResult> RemoveUnitAsync(Guid id, Guid unitId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RemoveProductUnitCommand(id, unitId), cancellationToken);
        return result.ToActionResult(this, NoContent);
    }

    /// <summary>P9: a manual stock adjustment (a new movement). Owner only.</summary>
    [HttpPost("{id:guid}/adjustments")]
    [Authorize(Policy = AuthPolicies.Owner)]
    [ProducesResponseType<StockMovementDto>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> AdjustAsync(Guid id, StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new AdjustStockCommand(id, request.QuantityChange, request.Note, request.UnitId), cancellationToken);
        return result.ToActionResult(this, movement => StatusCode(StatusCodes.Status201Created, movement));
    }

    /// <summary>P10: a stock count. Owner only.</summary>
    [HttpPost("{id:guid}/stock-counts")]
    [Authorize(Policy = AuthPolicies.Owner)]
    [ProducesResponseType<StockCountResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> CountAsync(Guid id, StockCountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new CountStockCommand(id, request.CountedQuantity, request.UnitId), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>P11: a page of the product's stock movements, newest first.</summary>
    [HttpGet("{id:guid}/movements")]
    [ProducesResponseType<PagedResponse<StockMovementDto>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> MovementsAsync(
        Guid id,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetProductMovementsQuery(id, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    private static string ProductLocation(Guid productId) => $"/api/products/{productId:D}";

    private static string UnitLocation(Guid productId, Guid unitId) => $"/api/products/{productId:D}/units/{unitId:D}";
}
