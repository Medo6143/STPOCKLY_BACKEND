namespace Stockly.Application.Abstractions.Identity;

/// <summary>
/// The shop of the current request, taken from the JWT (ADR-0002). Never from the request body.
/// <c>null</c> means no tenant: tenant queries return nothing and tenant writes are rejected.
/// </summary>
public interface ICurrentTenant
{
    Guid? ShopId { get; }
}
