using System.Security.Cryptography;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Limits;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Invoices;
using Stockly.Application.Features.Invoices.ScanInvoice;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using ContractInvoiceStatus = Stockly.Contracts.Invoices.InvoiceStatus;
using static Stockly.Application.Tests.Support.InvoiceTestContext;

namespace Stockly.Application.Tests.Features.Invoices;

public sealed class ScanInvoiceCommandHandlerTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly string ClientRequestId = Guid.CreateVersion7().ToString();

    private readonly InvoiceTestContext _context = new();

    [Fact]
    public async Task Handle_NewScan_UploadsInsertsQueuesAndCachesProcessingInvoice()
    {
        Invoice? added = null;
        _context.Invoices.When(repository => repository.Add(Arg.Any<Invoice>())).Do(call => added = call.Arg<Invoice>());

        using var request = new CancellationTokenSource();
        var result = await Handle(Command(), request.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Created.Should().BeTrue();
        added.Should().NotBeNull();
        added!.ClientRequestId.Should().Be(Guid.Parse(ClientRequestId));
        added.ImageSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Jpeg)));
        added.ImageObjectKey.Should().Be($"invoices/{ShopId:D}/2026/10/{added.Id:D}.jpg");
        result.Value.Invoice.Should().BeEquivalentTo(new InvoiceListItem(
            added.Id, ContractInvoiceStatus.Processing, null, null, null, null, 0, [], Now.UtcDateTime));

        await _context.Storage.Received(1).PutAsync(
            added.ImageObjectKey,
            Arg.Is<ReadOnlyMemory<byte>>(bytes => bytes.ToArray().SequenceEqual(Jpeg)),
            InvoiceImageFormat.Jpeg,
            Arg.Any<CancellationToken>());
        _context.UnitOfWork.SaveCount.Should().Be(1);
        // Cached before the job is queued: a fast job's NeedsReview must not be overwritten by this Processing.
        Received.InOrder(() =>
        {
            _context.StatusCache.SetAsync(
                ShopId,
                Arg.Is<InvoiceStatusSnapshot>(snapshot => snapshot.InvoiceId == added.Id && snapshot.Status == Domain.Invoices.InvoiceStatus.Processing),
                Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
            _context.Jobs.EnqueueProcessInvoice(ShopId, added.Id);
        });
    }

    [Fact]
    public async Task Handle_NoImage_ImageRequired()
    {
        var result = await Handle(new ScanInvoiceCommand(ClientRequestId, null));

        result.Error.Should().Be(InvoiceErrors.ImageRequired);
        await NothingStoredAsync();
    }

    [Fact]
    public async Task Handle_EmptyImage_ImageRequired()
    {
        var result = await Handle(Command(bytes: []));

        result.Error.Should().Be(InvoiceErrors.ImageRequired);
    }

    [Fact]
    public async Task Handle_ImageOverEightMegabytes_ImageTooLarge()
    {
        var command = new ScanInvoiceCommand(
            ClientRequestId, new InvoiceImageUpload(new MemoryStream(Jpeg), InvoiceImageFormat.Jpeg, ScanLimits.MaxImageBytes + 1L));

        var result = await Handle(command);

        result.Error.Should().Be(InvoiceErrors.ImageTooLarge);
        result.Error.Type.Should().Be(ErrorType.PayloadTooLarge);
        await NothingStoredAsync();
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData(null)]
    [InlineData(InvoiceImageFormat.Png)]
    public async Task Handle_TypeNotAcceptedOrNotMatchingBytes_UnsupportedImageType(string? contentType)
    {
        var result = await Handle(Command(contentType: contentType));

        result.Error.Should().Be(InvoiceErrors.UnsupportedImageType);
        await NothingStoredAsync();
    }

    [Fact]
    public async Task Handle_RepeatedClientRequestId_ReturnsExistingWithoutQuotaOrRateLimit()
    {
        var existing = ListItem(ContractInvoiceStatus.NeedsReview, Now.AddMinutes(-30));
        _context.Queries.FindListItemByClientRequestIdAsync(Guid.Parse(ClientRequestId), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await Handle(Command());

        result.Value.Should().Be(new ScanInvoiceResult(existing, Created: false));
        await _context.RateLimiter.DidNotReceive().TryAcquireAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _context.Quota.DidNotReceive().TryConsumeAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        _context.Jobs.DidNotReceive().EnqueueProcessInvoice(Arg.Any<Guid>(), Arg.Any<Guid>());
        await NothingStoredAsync();
    }

    [Fact]
    public async Task Handle_RepeatOfInvoiceStuckInProcessing_QueuesItsJobAgain()
    {
        var stale = ListItem(ContractInvoiceStatus.Processing, Now.AddSeconds(-_context.ScanOptions.RequeueAfterSeconds - 1));
        _context.Queries.FindListItemByClientRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(stale);

        var result = await Handle(Command());

        result.Value.Created.Should().BeFalse();
        _context.Jobs.Received(1).EnqueueProcessInvoice(ShopId, stale.Id);
    }

    [Fact]
    public async Task Handle_RepeatOfRecentProcessingInvoice_DoesNotQueueAgain()
    {
        var recent = ListItem(ContractInvoiceStatus.Processing, Now.AddSeconds(-30));
        _context.Queries.FindListItemByClientRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(recent);

        await Handle(Command());

        _context.Jobs.DidNotReceive().EnqueueProcessInvoice(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_RateLimited_ScanRateLimitedWithRetryAfterAndNoQuotaUsed()
    {
        _context.RateLimiter.TryAcquireAsync(ShopId, Arg.Any<CancellationToken>()).Returns(new RateLimitDecision(false, 17));

        var result = await Handle(Command());

        result.Error.Code.Should().Be(ScanErrors.RateLimitedCode);
        result.Error.Type.Should().Be(ErrorType.TooManyRequests);
        result.Error.RetryAfter.Should().Be(TimeSpan.FromSeconds(17));
        await _context.Quota.DidNotReceive().TryConsumeAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await NothingStoredAsync();
    }

    [Fact]
    public async Task Handle_DailyQuotaExceeded_RetryAfterIsTimeToCairoMidnight()
    {
        _context.Quota.TryConsumeAsync(ShopId, 30, Arg.Any<CancellationToken>()).Returns(ScanQuotaResult.Exceeded);

        var result = await Handle(Command());

        // Now is 07:00:30 UTC = 10:00:30 Cairo (EEST, UTC+3); Cairo midnight is 21:00 UTC.
        result.Error.Code.Should().Be(ScanErrors.DailyQuotaExceededCode);
        result.Error.RetryAfter.Should().Be(TimeSpan.FromHours(14) - TimeSpan.FromSeconds(30));
        await NothingStoredAsync();
    }

    [Fact]
    public async Task Handle_RedisDownAndTodayUnderQuota_CountsInvoicesInPostgresAndAllows()
    {
        _context.Quota.TryConsumeAsync(ShopId, 30, Arg.Any<CancellationToken>()).Returns(ScanQuotaResult.Unavailable);
        _context.Invoices.CountCreatedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(29);

        var result = await Handle(Command());

        result.Value.Created.Should().BeTrue();
        await _context.Invoices.Received(1).CountCreatedSinceAsync(
            new DateTimeOffset(2026, 10, 11, 21, 0, 0, TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RedisDownAndQuotaUsedInPostgres_DailyQuotaExceeded()
    {
        _context.Quota.TryConsumeAsync(ShopId, 30, Arg.Any<CancellationToken>()).Returns(ScanQuotaResult.Unavailable);
        _context.Invoices.CountCreatedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(30);

        var result = await Handle(Command());

        result.Error.Code.Should().Be(ScanErrors.DailyQuotaExceededCode);
    }

    [Fact]
    public async Task Handle_NoActiveSubscription_QuotaIsZero()
    {
        _context.Plans.GetDailyScanQuotaAsync(Arg.Any<CancellationToken>()).Returns((int?)null);
        _context.Quota.TryConsumeAsync(ShopId, 0, Arg.Any<CancellationToken>()).Returns(ScanQuotaResult.Exceeded);

        var result = await Handle(Command());

        result.Error.Code.Should().Be(ScanErrors.DailyQuotaExceededCode);
    }

    [Fact]
    public async Task Handle_UploadFails_ReleasesQuotaAndThrows()
    {
        _context.Storage.PutAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage down"));

        var act = () => Handle(Command());

        await act.Should().ThrowAsync<IOException>();
        await _context.Quota.Received(1).ReleaseAsync(ShopId, Arg.Any<CancellationToken>());
        _context.Invoices.DidNotReceive().Add(Arg.Any<Invoice>());
    }

    [Fact]
    public async Task Handle_ConcurrentFirstRequestWon_ReleasesQuotaDeletesImageAndReturnsWinner()
    {
        var winner = ListItem(ContractInvoiceStatus.Processing, Now);
        _context.Queries.FindListItemByClientRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(null, winner);
        _context.UnitOfWork.TrySaveResult = InvoiceErrors.DuplicateClientRequest;
        Invoice? loser = null;
        _context.Invoices.When(repository => repository.Add(Arg.Any<Invoice>())).Do(call => loser = call.Arg<Invoice>());

        var result = await Handle(Command());

        result.Value.Should().Be(new ScanInvoiceResult(winner, Created: false));
        await _context.Quota.Received(1).ReleaseAsync(ShopId, Arg.Any<CancellationToken>());
        await _context.Storage.Received(1).DeleteAsync(loser!.ImageObjectKey, Arg.Any<CancellationToken>());
        _context.Jobs.DidNotReceive().EnqueueProcessInvoice(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_RedisDownAndConcurrentRepeat_DoesNotReleaseUncountedQuota()
    {
        _context.Quota.TryConsumeAsync(ShopId, 30, Arg.Any<CancellationToken>()).Returns(ScanQuotaResult.Unavailable);
        _context.Queries.FindListItemByClientRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(null, ListItem(ContractInvoiceStatus.Processing, Now));
        _context.UnitOfWork.TrySaveResult = InvoiceErrors.DuplicateClientRequest;

        await Handle(Command());

        await _context.Quota.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClientRequestIdUsedByAnotherShop_CreatesThisShopsInvoice()
    {
        // The other shop's invoice is invisible to the tenant-filtered query (ADR-0002), so this shop gets its own.
        _context.Queries.FindListItemByClientRequestIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((InvoiceListItem?)null);

        var result = await Handle(Command());

        result.Value.Created.Should().BeTrue();
        _context.Invoices.Received(1).Add(Arg.Is<Invoice>(invoice => invoice.ShopId == ShopId));
    }

    [Fact]
    public async Task Handle_NoTenant_Unauthenticated()
    {
        _context.CurrentTenant.ShopId.Returns((Guid?)null);

        var result = await Handle(Command());

        result.Error.Should().Be(AuthErrors.Unauthenticated);
        await NothingStoredAsync();
    }

    private static ScanInvoiceCommand Command(byte[]? bytes = null, string? contentType = InvoiceImageFormat.Jpeg)
    {
        var content = bytes ?? Jpeg;
        return new ScanInvoiceCommand(ClientRequestId, new InvoiceImageUpload(new MemoryStream(content), contentType, content.Length));
    }

    private static InvoiceListItem ListItem(ContractInvoiceStatus status, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), status, null, null, null, null, 0, [], createdAt.UtcDateTime);

    private Task<Result<ScanInvoiceResult>> Handle(ScanInvoiceCommand command, CancellationToken cancellationToken = default) =>
        new ScanInvoiceCommandHandler(
                _context.CurrentUser,
                _context.CurrentTenant,
                _context.Invoices,
                _context.Queries,
                _context.Plans,
                _context.RateLimiter,
                _context.Quota,
                _context.Storage,
                _context.Jobs,
                _context.StatusCache,
                _context.UnitOfWork,
                _context.Clock,
                _context.ScanOptions)
            .Handle(command, cancellationToken);

    private async Task NothingStoredAsync()
    {
        await _context.Storage.DidNotReceive().PutAsync(
            Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _context.Invoices.DidNotReceive().Add(Arg.Any<Invoice>());
        _context.UnitOfWork.SaveCount.Should().Be(0);
    }
}
