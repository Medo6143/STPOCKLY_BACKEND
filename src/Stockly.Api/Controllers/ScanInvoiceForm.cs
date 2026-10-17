using Microsoft.AspNetCore.Mvc;

namespace Stockly.Api.Controllers;

/// <summary>The multipart form of contract I1. The controller maps it to <c>ScanInvoiceCommand</c>; Application never sees <see cref="IFormFile"/>.</summary>
public sealed class ScanInvoiceForm
{
    /// <summary>The invoice photo: <c>image/jpeg</c>, <c>image/png</c> or <c>image/webp</c>, at most 8 MB.</summary>
    [FromForm(Name = "image")]
    public IFormFile? Image { get; set; }

    /// <summary>A UUID created once per scan by the app and reused on every retry (ADR-0006).</summary>
    [FromForm(Name = "clientRequestId")]
    public string? ClientRequestId { get; set; }
}
