using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Errors;
using Stockly.Domain.Common;

namespace Stockly.Api.Extensions;

/// <summary>Maps a use-case <see cref="Result"/> to HTTP (AGENTS.md rule 28): the success mapping, or a contract ProblemDetails.</summary>
public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller, Func<T, IActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess ? onSuccess(result.Value) : controller.Problem(result.Error);
    }

    public static IActionResult ToActionResult(this Result result, ControllerBase controller, Func<IActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess ? onSuccess() : controller.Problem(result.Error);
    }

    private static ObjectResult Problem(this ControllerBase controller, Error error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var problem = StocklyProblems.FromError(controller.HttpContext, error);
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { StocklyProblems.ContentType } };
    }
}
