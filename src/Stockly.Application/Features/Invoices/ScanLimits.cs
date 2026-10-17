namespace Stockly.Application.Features.Invoices;

/// <summary>Contract constants of the scan upload (I1). Not configuration: changing them changes the contract.</summary>
public static class ScanLimits
{
    /// <summary>Largest accepted image: 8 MB.</summary>
    public const int MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>Largest multipart request: the image plus 1 MB for the form overhead.</summary>
    public const int MaxRequestBytes = MaxImageBytes + (1024 * 1024);
}
