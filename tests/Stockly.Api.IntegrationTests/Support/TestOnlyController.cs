using Microsoft.AspNetCore.Mvc;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>Endpoints that exist only in tests (added as an application part by <see cref="StocklyApiFactory"/>).</summary>
[ApiController]
[Route("__tests")]
public sealed class TestOnlyController : ControllerBase
{
    public const string SecretDetail = "connection to db-internal-7 failed with password hunter2";

    [HttpGet("throw")]
    public IActionResult Throw() =>
        throw new InvalidOperationException(SecretDetail + Request.Path);
}
