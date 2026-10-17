namespace Stockly.Application.Features.Invoices.ScanInvoice;

/// <summary>
/// The image types the scan accepts (contract I1). The declared content type must be one of them <b>and</b> agree with
/// the file's magic bytes: JPEG <c>FF D8 FF</c>, PNG <c>89 50 4E 47 0D 0A 1A 0A</c>, WebP <c>RIFF....WEBP</c>.
/// </summary>
public static class InvoiceImageFormat
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] RiffMagic = "RIFF"u8.ToArray();
    private static readonly byte[] WebpMagic = "WEBP"u8.ToArray();
    private const int WebpMarkerOffset = 8;

    /// <summary>The content type to store, or <c>null</c> when the type is not accepted or the bytes disagree with it.</summary>
    public static string? Detect(ReadOnlySpan<byte> content, string? declaredContentType)
    {
        var declared = Canonical(declaredContentType);
        return declared is not null && declared == Sniff(content) ? declared : null;
    }

    private static string? Sniff(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(JpegMagic))
        {
            return Jpeg;
        }

        if (content.StartsWith(PngMagic))
        {
            return Png;
        }

        return content.StartsWith(RiffMagic) && content.Length >= WebpMarkerOffset + WebpMagic.Length
            && content.Slice(WebpMarkerOffset, WebpMagic.Length).SequenceEqual(WebpMagic)
            ? Webp
            : null;
    }

    // "image/JPEG; charset=x" → "image/jpeg"
    private static string? Canonical(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        return mediaType is Jpeg or Png or Webp ? mediaType : null;
    }
}
