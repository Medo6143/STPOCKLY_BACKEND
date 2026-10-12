using Microsoft.AspNetCore.Mvc;

namespace Stockly.ArchitectureTests.Fixtures;

/// <summary>Uses only allowed types in its signatures.</summary>
public sealed class CleanController : ControllerBase
{
    public ActionResult<string> Get(Guid id) => Ok(id.ToString());
}
