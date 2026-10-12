using System.Net;
using Microsoft.AspNetCore.Http;
using Stockly.Api.Errors;
using Stockly.Api.RateLimiting;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;

namespace Stockly.Api.IntegrationTests.Errors;

/// <summary>v1.4 error mapping: <c>Locked</c> → 423, <c>TooManyRequests</c> → 429, and <c>Retry-After</c> from the error.</summary>
public sealed class RetryAfterMappingTests
{
    [Fact]
    public void FromError_LockedOutWithRetryAfter_Is423WithRetryAfterHeader()
    {
        var http = new DefaultHttpContext();

        var problem = StocklyProblems.FromError(http, AuthErrors.LockedOut with { RetryAfter = TimeSpan.FromMinutes(15) });

        problem.Status.Should().Be(StatusCodes.Status423Locked);
        problem.Extensions[StocklyProblems.CodeExtension].Should().Be("Auth.LockedOut");
        problem.Type.Should().NotBeNullOrEmpty();
        http.Response.Headers.RetryAfter.ToString().Should().Be("900");
    }

    [Fact]
    public void FromError_TooManyRequestsWithRetryAfter_Is429WithRetryAfterHeader()
    {
        var http = new DefaultHttpContext();

        var problem = StocklyProblems.FromError(http, AuthErrors.TooManyRequests with { RetryAfter = TimeSpan.FromSeconds(42) });

        problem.Status.Should().Be(StatusCodes.Status429TooManyRequests);
        problem.Extensions[StocklyProblems.CodeExtension].Should().Be("Auth.TooManyRequests");
        problem.Type.Should().NotBeNullOrEmpty();
        http.Response.Headers.RetryAfter.ToString().Should().Be("42");
    }

    [Fact]
    public void FromError_WithoutRetryAfter_SetsNoHeader()
    {
        var http = new DefaultHttpContext();

        StocklyProblems.FromError(http, AuthErrors.InvalidCredentials);

        http.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Theory]
    [InlineData(900_000, 900)]
    [InlineData(899_001, 900)]
    [InlineData(1_500, 2)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-5_000, 1)]
    public void RetryAfterSeconds_WholeSecondsRoundedUp_AtLeastOne(int milliseconds, long seconds)
    {
        StocklyProblems.RetryAfterSeconds(TimeSpan.FromMilliseconds(milliseconds)).Should().Be(seconds);
    }

    [Fact]
    public void ErrorTypes_LockedAndTooManyRequests_MapTo423And429()
    {
        StocklyProblems.StatusFor(ErrorType.Locked).Should().Be((int)HttpStatusCode.Locked);
        StocklyProblems.StatusFor(ErrorType.TooManyRequests).Should().Be((int)HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void PartitionKey_IsTheClientIp_OrOneSharedPartitionWithoutAnAddress()
    {
        var withAddress = new DefaultHttpContext();
        withAddress.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");

        AuthRateLimiting.PartitionKey(withAddress).Should().Be("198.51.100.7");
        AuthRateLimiting.PartitionKey(new DefaultHttpContext()).Should().Be(AuthRateLimiting.UnknownClientPartition);
    }
}
