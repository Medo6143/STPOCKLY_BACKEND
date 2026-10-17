using FluentValidation;
using NSubstitute;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Jobs;
using Stockly.Application.Abstractions.Limits;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Storage;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Invoices;
using Stockly.Application.Features.Invoices.Matching;
using Stockly.Application.Features.Invoices.ProcessInvoice;
using Stockly.Application.Features.Invoices.Validation;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;

namespace Stockly.Application.Tests.Support;

/// <summary>
/// Substitutes for every port the invoice handlers use: a user of <see cref="ShopId"/> signed in, a fixed clock, a unit
/// of work that runs its transaction inline. Repositories are tenant-filtered in production, so "another shop's
/// invoice" is a repository returning null.
/// </summary>
internal sealed class InvoiceTestContext
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 7, 0, 30, TimeSpan.Zero);
    public static readonly Guid UserId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000a1");
    public static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");
    public static readonly string Sha256 = new('a', InvoiceLimits.ImageSha256Length);

    public InvoiceTestContext()
    {
        Clock.UtcNow.Returns(_ => Now);
        CurrentUser.UserId.Returns(UserId);
        CurrentTenant.ShopId.Returns(ShopId);
        RateLimiter.TryAcquireAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(RateLimitDecision.Allow);
        Quota.TryConsumeAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ScanQuotaResult.Allowed);
        Plans.GetDailyScanQuotaAsync(Arg.Any<CancellationToken>()).Returns(30);
        UnitOfWork.TrySaveResult = Result.Success();
        Invoices.FindForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Stored.Find(invoice => invoice.Id == call.Arg<Guid>()));
        Invoices.FindProcessingInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Stored.Find(invoice => invoice.Id == call.Arg<Guid>()) is { } invoice
                ? new InvoiceProcessingInfo(invoice.Status, invoice.ImageObjectKey, invoice.ImageSha256)
                : null);
        Products.FindByNormalizedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<NamedProduct>());
        Products.FindUnitsInfoAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProductUnitsInfo>());
        Aliases.FindExactAsync(Arg.Any<Guid?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AliasMatch>());
        UsageRecords.When(repository => repository.Add(Arg.Any<AiUsageRecord>()))
            .Do(call => AddedUsage.Add(call.Arg<AiUsageRecord>()));
    }

    public IClock Clock { get; } = Substitute.For<IClock>();

    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();

    public ICurrentTenant CurrentTenant { get; } = Substitute.For<ICurrentTenant>();

    public IInvoiceRepository Invoices { get; } = Substitute.For<IInvoiceRepository>();

    public IInvoiceQueries Queries { get; } = Substitute.For<IInvoiceQueries>();

    public IShopPlanQueries Plans { get; } = Substitute.For<IShopPlanQueries>();

    public IScanRateLimiter RateLimiter { get; } = Substitute.For<IScanRateLimiter>();

    public IScanQuotaCounter Quota { get; } = Substitute.For<IScanQuotaCounter>();

    public IFileStorage Storage { get; } = Substitute.For<IFileStorage>();

    public IBackgroundJobScheduler Jobs { get; } = Substitute.For<IBackgroundJobScheduler>();

    public IInvoiceStatusCache StatusCache { get; } = Substitute.For<IInvoiceStatusCache>();

    public IAiResultCache AiCache { get; } = Substitute.For<IAiResultCache>();

    public IAiInvoiceExtractor Extractor { get; } = Substitute.For<IAiInvoiceExtractor>();

    public ISupplierRepository Suppliers { get; } = Substitute.For<ISupplierRepository>();

    public IAiUsageRecordRepository UsageRecords { get; } = Substitute.For<IAiUsageRecordRepository>();

    public IProductRepository Products { get; } = Substitute.For<IProductRepository>();

    public IProductAliasRepository Aliases { get; } = Substitute.For<IProductAliasRepository>();

    public InlineUnitOfWork UnitOfWork { get; } = new();

    public ScanOptions ScanOptions { get; } = new();

    /// <summary>Invoices the tenant-filtered repository can see.</summary>
    public List<Invoice> Stored { get; } = [];

    /// <summary>Usage rows added through the repository.</summary>
    public List<AiUsageRecord> AddedUsage { get; } = [];

    public IValidator<ExtractedInvoice> OutputValidator { get; } = new ExtractedInvoiceValidator();

    public InvoiceValidator InvoiceValidator() => new(
        [new ItemCapExceededRule(), new UnreadableLinesRule(), new TotalMismatchRule(), new DuplicateInvoiceNumberRule(Invoices)]);

    /// <summary>The production matcher with both v1 strategies over the substituted repositories.</summary>
    public ProductMatcher Matcher() => new([new ExactNameMatchStrategy(Products), new ExactAliasMatchStrategy(Aliases)]);

    /// <summary>A processing invoice of the shop that the repository finds.</summary>
    public Invoice ProcessingInvoice(string? sha256 = null)
    {
        var invoice = Invoice.CreateForScan(
            ShopId, Guid.CreateVersion7(), UserId, sha256 ?? Sha256, Now.AddMinutes(-1), id => InvoiceImageKeys.For(ShopId, id, Now)).Value;
        Stored.Add(invoice);
        return invoice;
    }
}

/// <summary>Runs the operation inline (no database) and records commits, so handlers' transactions can be asserted.</summary>
internal sealed class InlineUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public int CommitCount { get; private set; }

    public Result TrySaveResult { get; set; } = Result.Success();

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, Func<TResult, bool> shouldCommit, CancellationToken cancellationToken)
    {
        var result = await operation(cancellationToken);
        if (shouldCommit(result))
        {
            CommitCount++;
        }

        return result;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.FromResult(TrySaveResult);
    }
}
