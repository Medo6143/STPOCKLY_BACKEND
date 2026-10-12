namespace Stockly.Application.Abstractions.Persistence;

public interface IPlanRepository
{
    /// <summary>The id of the active plan with this code, or <c>null</c>.</summary>
    Task<Guid?> FindActiveIdByCodeAsync(string code, CancellationToken cancellationToken);
}
