using NSubstitute;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Application.Behaviors;
using Stockly.Domain.Common;

namespace Stockly.Application.Tests.Behaviors;

/// <summary>AGENTS.md rule 23: the catalog version is bumped after commit, only for successful writes.</summary>
public sealed class CatalogCacheInvalidationBehaviorTests
{
    private static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");

    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    public CatalogCacheInvalidationBehaviorTests()
    {
        _tenant.ShopId.Returns(ShopId);
    }

    [Fact]
    public async Task Handle_Success_BumpsTheShopsCatalogVersionAfterTheInnerPipelineReturned()
    {
        var events = new List<string>();
        _cache.IncrementVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("bump");
                return Task.CompletedTask;
            });

        var result = await Behavior().Handle(
            new CatalogWrite(),
            _ =>
            {
                events.Add("transaction committed");
                return Task.FromResult(Result.Success());
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        events.Should().Equal("transaction committed", "bump");
        await _cache.Received(1).IncrementVersionAsync($"stockly:{ShopId}:ver:catalog", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RequestCancelledRightAfterTheCommit_StillBumps()
    {
        // The client disconnects just after the sale committed: the bump must still reach Redis, or the retry returns
        // the existing sale and the old quantities stay cached.
        using var request = new CancellationTokenSource();

        var result = await Behavior().Handle(
            new CatalogWrite(),
            async _ =>
            {
                await request.CancelAsync();
                return Result.Success();
            },
            request.Token);

        result.IsSuccess.Should().BeTrue();
        await _cache.Received(1).IncrementVersionAsync(
            $"stockly:{ShopId}:ver:catalog", Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
    }

    [Fact]
    public async Task Handle_Failure_DoesNotBump()
    {
        var result = await Behavior().Handle(
            new CatalogWrite(), _ => Task.FromResult(Result.Failure(Error.Conflict("X.Y", "m"))), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _cache.DidNotReceive().IncrementVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoTenant_DoesNotBump()
    {
        _tenant.ShopId.Returns((Guid?)null);

        await Behavior().Handle(new CatalogWrite(), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        await _cache.DidNotReceive().IncrementVersionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Registration_InvalidationRunsOutsideTheTransaction()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddApplication();

        var behaviors = services
            .Where(descriptor => descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(MediatR.IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        behaviors.IndexOf(typeof(CatalogCacheInvalidationBehavior<,>))
            .Should().BeLessThan(behaviors.IndexOf(typeof(TransactionBehavior<,>)), "outer behaviors run first and finish last");
    }

    private CatalogCacheInvalidationBehavior<CatalogWrite, Result> Behavior() => new(_cache, _tenant);

    private sealed record CatalogWrite : ICommand, IInvalidatesCatalog;
}
