using Stockly.Domain.Sales;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Sale writes for the current shop (tenant-stamped on save). Stock changes go through <c>IStockService</c>.</summary>
public interface ISaleRepository
{
    void Add(Sale sale);
}
