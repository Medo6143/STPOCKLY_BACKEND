using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Errors;
using Stockly.Api.Extensions;
using Stockly.Application.Features.Sales.CreateSale;
using Stockly.Application.Features.Sales.GetSale;
using Stockly.Application.Features.Sales.GetSales;
using Stockly.Application.Validation;
using Stockly.Contracts.Common;
using Stockly.Contracts.Sales;

namespace Stockly.Api.Controllers;

/// <summary>
/// Contract §7: S1–S3 (Sprint 4). Maps HTTP to MediatR and back; no business decisions here (AGENTS.md rule 26). The shop
/// always comes from the token (ADR-0002).
/// </summary>
[ApiController]
[Route("api/sales")]
[Authorize]
public sealed class SalesController(ISender sender) : ControllerBase
{
    private const string Json = "application/json";

    /// <summary>
    /// S1: record a sale and take its stock out. A new sale is <c>201</c>; a repeat of the same <c>clientRequestId</c> is
    /// <c>200</c> with the existing sale. Negative stock is a warning, never an error.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<SaleDto>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> CreateAsync(CreateSaleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new CreateSaleCommand(request.ClientRequestId, request.Items, request.Note), cancellationToken);
        return result.ToCreatedOrOk(this, sale => sale.Created, sale => sale.Sale, sale => SaleLocation(sale.Sale.Id));
    }

    /// <summary>S2: a page of the shop's sales, newest first, optionally created at or after <c>from</c> and before <c>to</c>.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<SaleListItem>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = Paging.DefaultPage,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetSalesQuery(from, to, page, pageSize), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    /// <summary>S3: one sale with its lines.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, StocklyProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, StocklyProblems.ContentType)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSaleQuery(id), cancellationToken);
        return result.ToActionResult(this, Ok);
    }

    private static string SaleLocation(Guid saleId) => $"/api/sales/{saleId:D}";
}
