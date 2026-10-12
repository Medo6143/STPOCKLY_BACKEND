namespace Stockly.Domain.Common;

/// <summary>Marks an entity owned by one shop. Every implementing type gets the EF tenant query filter (ADR-0002).</summary>
public interface ITenantEntity
{
    Guid ShopId { get; }
}
