using Stockly.Application.Features.Invoices.ScanInvoice;

namespace Stockly.Application.Tests.Features.Invoices;

/// <summary>I1 request shape (ScanInvoiceCommandValidator) and the image type check (InvoiceImageFormat).</summary>
public sealed class ScanInvoiceValidationTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xDB];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8, .. "VP8 "u8];

    private readonly ScanInvoiceCommandValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Validator_ClientRequestIdMissingOrNotAUuid_Fails(string? clientRequestId)
    {
        var result = _validator.Validate(new ScanInvoiceCommand(clientRequestId, null));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(ScanInvoiceCommand.ClientRequestId));
    }

    [Fact]
    public void Validator_Uuid_Passes()
    {
        _validator.Validate(new ScanInvoiceCommand(Guid.CreateVersion7().ToString(), null)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("image/jpeg", "image/jpeg")]
    [InlineData("IMAGE/JPEG; charset=binary", "image/jpeg")]
    public void Detect_JpegDeclaredAndSniffed_ReturnsCanonicalType(string declared, string expected)
    {
        InvoiceImageFormat.Detect(Jpeg, declared).Should().Be(expected);
    }

    [Fact]
    public void Detect_PngAndWebp_Accepted()
    {
        InvoiceImageFormat.Detect(Png, InvoiceImageFormat.Png).Should().Be(InvoiceImageFormat.Png);
        InvoiceImageFormat.Detect(Webp, InvoiceImageFormat.Webp).Should().Be(InvoiceImageFormat.Webp);
    }

    [Fact]
    public void Detect_DeclaredTypeDisagreesWithBytes_Rejected()
    {
        InvoiceImageFormat.Detect(Png, InvoiceImageFormat.Jpeg).Should().BeNull();
        InvoiceImageFormat.Detect(Jpeg, InvoiceImageFormat.Webp).Should().BeNull();
    }

    [Fact]
    public void Detect_RiffWithoutWebpMarker_Rejected()
    {
        byte[] wav = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WAVE"u8];

        InvoiceImageFormat.Detect(wav, InvoiceImageFormat.Webp).Should().BeNull();
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("image/jpg")]
    [InlineData(null)]
    public void Detect_TypeOutsideTheContract_Rejected(string? declared)
    {
        InvoiceImageFormat.Detect(Jpeg, declared).Should().BeNull();
    }
}
