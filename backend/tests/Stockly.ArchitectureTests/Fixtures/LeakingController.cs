using Microsoft.AspNetCore.Mvc;
using Stockly.ArchitectureTests.Fixtures.ForbiddenModel;

namespace Stockly.ArchitectureTests.Fixtures;

/// <summary>Exposes a forbidden type in a generic return, a parameter and a nested array.</summary>
public sealed class LeakingController : ControllerBase
{
    public Task<ActionResult<ForbiddenEntity>> GetAsync() =>
        Task.FromResult<ActionResult<ForbiddenEntity>>(Ok(new ForbiddenEntity()));

    public IActionResult Update(ForbiddenEntity entity) => Ok(entity);

    public ActionResult<ForbiddenEntity[]> ListArray() => Ok(Array.Empty<ForbiddenEntity>());
}
