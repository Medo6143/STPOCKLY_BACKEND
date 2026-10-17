using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Stockly.Api.Errors;
using Stockly.Domain.Invoices;

namespace Stockly.Api.Controllers;

/// <summary>
/// Reads the scan's multipart form before model binding, so a body over the request limit (Kestrel's
/// <c>BadHttpRequestException</c> 413, or the form reader's multipart length limit) becomes the contract's
/// <c>413 Invoice.ImageTooLarge</c> (docs/design/backend.md §3.1) instead of a model-binding 400. HTTP mapping only.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ScanUploadLimitAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.HttpContext.Request.HasFormContentType)
        {
            try
            {
                await context.HttpContext.Request.ReadFormAsync(context.HttpContext.RequestAborted);
            }
            catch (Exception exception) when (IsTooLarge(exception))
            {
                var problem = StocklyProblems.FromError(context.HttpContext, InvoiceErrors.ImageTooLarge);
                context.Result = new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { StocklyProblems.ContentType } };
                return;
            }
        }

        await next();
    }

    // The form reader reports its length limits as InvalidDataException; Kestrel's body size limit is a 413 BadHttpRequestException.
    private static bool IsTooLarge(Exception exception) =>
        exception is InvalidDataException
        || (exception is BadHttpRequestException badRequest && badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge);
}
