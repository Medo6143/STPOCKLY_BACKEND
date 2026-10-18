using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Api.IntegrationTests.Support;
using Stockly.Application.Features.Admin.ExpireInvoiceImages;

namespace Stockly.Api.IntegrationTests.Admin;

/// <summary>Image lifecycle (docs/operations/image-lifecycle.md): after the retention run AD18 answers <c>imageUrl: null</c>.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class ImageRetentionEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task RetentionRun_ImageOlderThan90Days_AdminDetailWithoutImageUrl_RecentStillSigned()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var old = await InvoiceApi.ScanAsync(client, shop.AccessToken);
        var recent = await InvoiceApi.ScanAsync(client, shop.AccessToken);
        await fixture.ExecuteAsync("UPDATE invoices SET created_at = now() - interval '91 days' WHERE id = @p0", old);

        int marked;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ExpireInvoiceImagesCommand());
            marked = result.Value;
        }

        var admin = await AdminApi.LoginAsync(client);
        var oldDetail = await AdminApi.GetAsync(client, AdminApi.Invoice(old), admin.AccessToken);
        var recentDetail = await AdminApi.GetAsync(client, AdminApi.Invoice(recent), admin.AccessToken);

        marked.Should().BeGreaterThanOrEqualTo(1);
        (await fixture.ScalarAsync<bool>("SELECT image_deleted_at IS NOT NULL FROM invoices WHERE id = @p0", old)).Should().BeTrue();
        oldDetail.Status.Should().Be(HttpStatusCode.OK, oldDetail.Raw);
        oldDetail.Body!["imageUrl"].Should().BeNull("the lifecycle rule has expired the object");
        oldDetail.Body["invoice"]!["id"]!.GetValue<Guid>().Should().Be(old, "the rest of the invoice stays readable");
        recentDetail.Body!["imageUrl"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }
}
