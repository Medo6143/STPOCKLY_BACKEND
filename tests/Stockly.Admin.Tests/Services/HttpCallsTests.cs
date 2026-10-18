using System.Net;
using System.Text;
using Stockly.Admin.Services;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Services;

public sealed class HttpCallsTests : IDisposable
{
    private readonly FakeApi _api = new();

    [Fact]
    public async Task Get_ProblemDetails_ReadsCodeDetailTraceIdAndErrors()
    {
        _api.On(HttpMethod.Get, ApiRoutes.Plans, HttpStatusCode.BadRequest, new
        {
            title = "Bad Request",
            status = 400,
            detail = "Fix the fields",
            code = "Validation.Failed",
            traceId = Samples.TraceId,
            errors = new Dictionary<string, string[]> { ["pageSize"] = ["Too large"] },
        });

        var result = await Client().GetPlansAsync(CancellationToken.None);

        result.Problem.Should().BeEquivalentTo(new
        {
            Status = 400,
            Code = "Validation.Failed",
            Message = "Fix the fields",
            TraceId = Samples.TraceId,
        });
        result.Problem!.Errors["pageSize"].Should().Equal("Too large");
    }

    [Fact]
    public async Task Get_HtmlErrorPage_ReturnsHttpStatusProblem()
    {
        _api.On(HttpMethod.Get, ApiRoutes.Plans, () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>bad gateway</html>", Encoding.UTF8, "text/html"),
        }));

        var result = await Client().GetPlansAsync(CancellationToken.None);

        result.Problem!.Code.Should().Be(ClientErrorCodes.HttpStatus);
        result.Problem.Status.Should().Be(502);
    }

    [Fact]
    public async Task Get_NetworkFailure_ReturnsUnreachableProblem()
    {
        _api.On(HttpMethod.Get, ApiRoutes.Plans, () => throw new HttpRequestException("CORS"));

        var result = await Client().GetPlansAsync(CancellationToken.None);

        result.Problem!.Code.Should().Be(ClientErrorCodes.Unreachable);
    }

    [Fact]
    public async Task Get_MoneyString_StaysAString()
    {
        _api.OnGet(ApiRoutes.Plans, new[] { new PlanDto(Guid.NewGuid(), "pro", "Pro", 50, 5, "299.90", true) });

        var result = await Client().GetPlansAsync(CancellationToken.None);

        result.Value!.Single().MonthlyPrice.Should().Be("299.90");
    }

    private AdminApiClient Client() => new(new HttpClient(_api) { BaseAddress = FakeApi.BaseAddress });

    public void Dispose() => _api.Dispose();
}
