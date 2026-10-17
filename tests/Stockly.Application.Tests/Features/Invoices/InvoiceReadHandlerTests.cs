using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Features.Invoices.GetInvoice;
using Stockly.Application.Features.Invoices.GetInvoices;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using static Stockly.Application.Tests.Support.InvoiceTestContext;
using ContractInvoiceStatus = Stockly.Contracts.Invoices.InvoiceStatus;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;

namespace Stockly.Application.Tests.Features.Invoices;

/// <summary>I3 (status cache, read-through, fallback) and I2 handlers.</summary>
public sealed class InvoiceReadHandlerTests
{
    private static readonly Guid InvoiceId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000c9");
    private static readonly Guid ClientRequestId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000ca");
    private static readonly DateTimeOffset CreatedAt = Now.AddMinutes(-1);

    private readonly InvoiceTestContext _context = new();

    [Fact]
    public async Task GetInvoice_ProcessingInCache_AnsweredFromTheSnapshotWithoutPostgres()
    {
        _context.StatusCache.GetAsync(ShopId, InvoiceId, Arg.Any<CancellationToken>())
            .Returns(new InvoiceStatusSnapshot(InvoiceId, DomainInvoiceStatus.Processing, CreatedAt, ClientRequestId, CreatedAt));

        var result = await GetInvoice(InvoiceId);

        result.Value.Should().BeEquivalentTo(new InvoiceDetail(
            InvoiceId, ClientRequestId, ContractInvoiceStatus.Processing, null, null, null, null, null, null, [], null, [], CreatedAt.UtcDateTime, null, null));
        await _context.Queries.DidNotReceiveWithAnyArgs().FindDetailAsync(default, default);
    }

    [Fact]
    public async Task GetInvoice_CacheSaysNeedsReview_ReadsPostgresAndDoesNotRewriteTheCache()
    {
        _context.StatusCache.GetAsync(ShopId, InvoiceId, Arg.Any<CancellationToken>())
            .Returns(new InvoiceStatusSnapshot(InvoiceId, DomainInvoiceStatus.NeedsReview, Now, ClientRequestId, CreatedAt));
        var detail = Detail(ContractInvoiceStatus.NeedsReview);
        _context.Queries.FindDetailAsync(InvoiceId, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await GetInvoice(InvoiceId);

        result.Value.Should().BeSameAs(detail);
        await _context.StatusCache.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default);
    }

    [Fact]
    public async Task GetInvoice_CacheMissOrRedisDown_ReadsPostgresAndWritesTheProcessingSnapshotBack()
    {
        // A Redis failure is a miss inside the adapter (rule 25), so the handler sees null.
        _context.StatusCache.GetAsync(ShopId, InvoiceId, Arg.Any<CancellationToken>()).Returns((InvoiceStatusSnapshot?)null);
        _context.Queries.FindDetailAsync(InvoiceId, Arg.Any<CancellationToken>()).Returns(Detail(ContractInvoiceStatus.Processing));

        var result = await GetInvoice(InvoiceId);

        result.Value.Status.Should().Be(ContractInvoiceStatus.Processing);
        await _context.StatusCache.Received(1).SetAsync(
            ShopId,
            new InvoiceStatusSnapshot(InvoiceId, DomainInvoiceStatus.Processing, CreatedAt, ClientRequestId, CreatedAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInvoice_MissingOrAnotherShops_NotFound()
    {
        // The cache key carries the caller's shop, and the projection is tenant-filtered: another shop's id misses both.
        var result = await GetInvoice(InvoiceId);

        result.Error.Should().Be(InvoiceErrors.NotFound);
        await _context.StatusCache.Received(1).GetAsync(ShopId, InvoiceId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInvoice_NoTenant_NotFoundWithoutReadingAnything()
    {
        _context.CurrentTenant.ShopId.Returns((Guid?)null);

        var result = await GetInvoice(InvoiceId);

        result.Error.Should().Be(InvoiceErrors.NotFound);
        await _context.StatusCache.DidNotReceiveWithAnyArgs().GetAsync(default, default, default);
    }

    [Fact]
    public async Task GetInvoices_StatusFilter_PassedToTheQueryAsTheDomainStatus()
    {
        var page = new PagedResponse<InvoiceListItem>([], 2, 10, 0);
        _context.Queries.ListAsync(DomainInvoiceStatus.Confirmed, 2, 10, Arg.Any<CancellationToken>()).Returns(page);

        var result = await new GetInvoicesQueryHandler(_context.Queries)
            .Handle(new GetInvoicesQuery(ContractInvoiceStatus.Confirmed, 2, 10), CancellationToken.None);

        result.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task GetInvoices_NoStatus_AllStatuses()
    {
        var page = new PagedResponse<InvoiceListItem>([], 1, 20, 0);
        _context.Queries.ListAsync(null, 1, 20, Arg.Any<CancellationToken>()).Returns(page);

        var result = await new GetInvoicesQueryHandler(_context.Queries).Handle(new GetInvoicesQuery(null, 1, 20), CancellationToken.None);

        result.Value.Should().BeSameAs(page);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void GetInvoicesValidator_BadPaging_Fails(int page, int pageSize)
    {
        new GetInvoicesQueryValidator().Validate(new GetInvoicesQuery(null, page, pageSize)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void GetInvoicesValidator_UnknownStatus_Fails()
    {
        new GetInvoicesQueryValidator().Validate(new GetInvoicesQuery((ContractInvoiceStatus)99, 1, 20)).IsValid.Should().BeFalse();
    }

    private static InvoiceDetail Detail(ContractInvoiceStatus status) =>
        new(InvoiceId, ClientRequestId, status, null, null, null, null, null, null, [], null, [], CreatedAt.UtcDateTime, null, null);

    private Task<Result<InvoiceDetail>> GetInvoice(Guid id) =>
        new GetInvoiceQueryHandler(_context.CurrentTenant, _context.StatusCache, _context.Queries)
            .Handle(new GetInvoiceQuery(id), CancellationToken.None);
}
