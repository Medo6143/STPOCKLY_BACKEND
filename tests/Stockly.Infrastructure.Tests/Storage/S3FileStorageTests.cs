using Amazon.S3;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Storage;

/// <summary>The image storage adapter against a real MinIO (ADR-0008).</summary>
[Collection(MinioCollectionDefinition.Name)]
public sealed class S3FileStorageTests(MinioFixture fixture)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    [Fact]
    public async Task PutThenGet_RoundTripsBytesAndRealContentType()
    {
        var storage = fixture.CreateStorage();
        var key = NewKey();

        await storage.PutAsync(key, Png, "image/png", CancellationToken.None);
        var stored = await storage.GetAsync(key, CancellationToken.None);

        stored.Should().NotBeNull();
        stored!.Content.ToArray().Should().Equal(Png);
        stored.ContentType.Should().Be("image/png", "the key ends in .jpg for every type; the object keeps its real type");
    }

    [Fact]
    public async Task Put_SliceOfALargerBuffer_StoresOnlyTheSlice()
    {
        var storage = fixture.CreateStorage();
        var key = NewKey();
        var pooled = new byte[64];
        Png.CopyTo(pooled, 0);

        await storage.PutAsync(key, pooled.AsMemory(0, Png.Length), "image/png", CancellationToken.None);

        (await storage.GetAsync(key, CancellationToken.None))!.Content.ToArray().Should().Equal(Png);
    }

    [Fact]
    public async Task Get_MissingObject_ReturnsNull()
    {
        (await fixture.CreateStorage().GetAsync(NewKey(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesTheObjectAndIgnoresAMissingOne()
    {
        var storage = fixture.CreateStorage();
        var key = NewKey();
        await storage.PutAsync(key, Png, "image/png", CancellationToken.None);

        await storage.DeleteAsync(key, CancellationToken.None);
        var deleteAgain = () => storage.DeleteAsync(key, CancellationToken.None);

        (await storage.GetAsync(key, CancellationToken.None)).Should().BeNull();
        await deleteAgain.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Put_UnknownBucket_Throws()
    {
        var storage = fixture.CreateStorage("bucket-that-does-not-exist");

        var act = () => storage.PutAsync(NewKey(), Png, "image/png", CancellationToken.None);

        await act.Should().ThrowAsync<AmazonS3Exception>("a storage failure is not a missing object: the scan returns 500 and releases the quota");
    }

    [Fact]
    public async Task GetDownloadUrl_StoredObject_IsAPresignedGetThatServesTheBytes()
    {
        var storage = fixture.CreateStorage();
        var key = NewKey();
        await storage.PutAsync(key, Png, "image/png", CancellationToken.None);

        var url = await storage.GetDownloadUrlAsync(key, TimeSpan.FromMinutes(5), CancellationToken.None);

        url.Should().NotBeNull().And.StartWith("http://").And.Contain("X-Amz-Signature");
        url.Should().MatchRegex("X-Amz-Expires=(29[89]|30[01])&", "valid five minutes, give or take the SDK's own rounding");
        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(new Uri(url!));
        bytes.Should().Equal(Png);
    }

    [Fact]
    public async Task GetDownloadUrl_MissingObject_IsNull()
    {
        var storage = fixture.CreateStorage();

        (await storage.GetDownloadUrlAsync(NewKey(), TimeSpan.FromMinutes(5), CancellationToken.None)).Should().BeNull();
    }

    private static string NewKey() => $"invoices/{Guid.CreateVersion7()}/2026/10/{Guid.CreateVersion7()}.jpg";
}
