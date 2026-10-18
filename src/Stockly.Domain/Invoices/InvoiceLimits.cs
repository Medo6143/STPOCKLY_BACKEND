namespace Stockly.Domain.Invoices;

/// <summary>Limits of an invoice from the contract (I4) and AGENTS.md rule 15.</summary>
public static class InvoiceLimits
{
    /// <summary>At most 100 lines per invoice; <c>line_no</c> is 1..100.</summary>
    public const int MaxLines = 100;

    /// <summary>Longest invoice number and batch number.</summary>
    public const int NumberMaxLength = 64;

    /// <summary>Longest supplier name.</summary>
    public const int SupplierNameMaxLength = 200;

    /// <summary>Longest raw product name of a line.</summary>
    public const int LineNameMaxLength = 300;

    /// <summary>Length of <c>image_sha256</c>: lower-case hex of SHA-256.</summary>
    public const int ImageSha256Length = 64;

    /// <summary>
    /// Days an invoice image is kept. The storage lifecycle rule (<c>minio-init</c>, <c>mc ilm rule add --expire-days</c>)
    /// uses the same number; docs/operations/image-lifecycle.md.
    /// </summary>
    public const int ImageRetentionDays = 90;
}
