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

    /// <summary>
    /// For idempotent creates (ADR-0006): a value this request created is <c>202 Accepted</c> with <c>Location</c>; a
    /// repeat that found the existing resource is <c>200 OK</c>. HTTP mapping only; the handler decides <paramref name="isNew"/>.
    /// </summary>
    public static IActionResult ToAcceptedOrOk<T>(
        this Result<T> result,
        ControllerBase controller,
        Func<T, bool> isNew,
        Func<T, object> body,
        Func<T, string> location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(isNew);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(location);

        if (result.IsFailure)
        {
            return controller.Problem(result.Error);
        }

        var value = result.Value;
        return isNew(value) ? controller.Accepted(location(value), body(value)) : controller.Ok(body(value));
    }

    /// <summary>
    /// For idempotent creates that finish in the request (ADR-0006, S1): a value this request created is <c>201 Created</c>
    /// with <c>Location</c>; a repeat that found the existing resource is <c>200 OK</c>. The handler decides <paramref name="isNew"/>.
    /// </summary>
    public static IActionResult ToCreatedOrOk<T>(
        this Result<T> result,
        ControllerBase controller,
        Func<T, bool> isNew,
        Func<T, object> body,
        Func<T, string> location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(isNew);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(location);

        if (result.IsFailure)
        {
            return controller.Problem(result.Error);
        }

        var value = result.Value;
        return isNew(value) ? controller.Created(location(value), body(value)) : controller.Ok(body(value));
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
