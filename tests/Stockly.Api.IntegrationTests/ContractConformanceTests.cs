using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests;

/// <summary>
/// docs/api/contract.md (human contract) and docs/api/openapi.json (generated) must agree: every operation in the
/// spec is an "implemented" row of the contract's endpoint index with the same status codes, and every
/// implemented row exists in the spec.
/// </summary>
public sealed partial class ContractConformanceTests
{
    private const string ContractPath = "docs/api/contract.md";
    private const string Implemented = "implemented";
    private const int MinimumIndexRows = 27;

    private static readonly string[] HttpMethods = ["get", "post", "put", "delete", "patch"];

    [Fact]
    public void ContractIndex_Parses_EveryRowOnce()
    {
        var rows = ContractRows();

        rows.Should().HaveCountGreaterThanOrEqualTo(MinimumIndexRows, "the index must be parsed, or this check passes vacuously");
        rows.Select(row => row.Operation).Should().OnlyHaveUniqueItems();
        rows.Select(row => row.State).Should().OnlyContain(state => state == Implemented || state == "planned");
    }

    [Fact]
    public void OpenApi_EveryOperation_IsAnImplementedContractRowWithSameStatuses()
    {
        var contract = ContractRows().ToDictionary(row => row.Operation);

        var problems = SpecOperations()
            .Select(spec => contract.TryGetValue(spec.Operation, out var row)
                ? row.State != Implemented
                    ? $"{spec.Operation}: live but marked '{row.State}' in the contract"
                    : !row.Statuses.Order().SequenceEqual(spec.Statuses.Order())
                        ? $"{spec.Operation}: statuses [{string.Join(", ", spec.Statuses.Order())}] in the spec, [{string.Join(", ", row.Statuses.Order())}] in the contract"
                        : null
                : $"{spec.Operation}: not in the contract index")
            .OfType<string>();

        problems.Should().BeEmpty("docs/api/openapi.json and docs/api/contract.md must agree");
    }

    [Fact]
    public void Contract_EveryImplementedRow_ExistsInOpenApi()
    {
        var spec = SpecOperations().Select(operation => operation.Operation).ToHashSet();

        var missing = ContractRows()
            .Where(row => row.State == Implemented && !spec.Contains(row.Operation))
            .Select(row => row.Operation);

        missing.Should().BeEmpty("a row marked implemented must be live in docs/api/openapi.json");
    }

    private static List<ContractRow> ContractRows() =>
        File.ReadLines(ComposeImages.RepoFile(ContractPath))
            .Select(line => IndexRow().Match(line))
            .Where(match => match.Success)
            .Select(match => new ContractRow(
                $"{match.Groups["method"].Value} {match.Groups["path"].Value}",
                match.Groups["statuses"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                match.Groups["state"].Value))
            .ToList();

    private static IEnumerable<(string Operation, string[] Statuses)> SpecOperations()
    {
        var paths = OpenApiSpec.LoadCommitted()["paths"]?.AsObject() ?? [];
        foreach (var (path, item) in paths)
        {
            foreach (var method in HttpMethods)
            {
                if (item?[method] is JsonObject operation)
                {
                    var statuses = operation["responses"]?.AsObject().Select(response => response.Key).ToArray() ?? [];
                    yield return ($"{method.ToUpperInvariant()} {path}", statuses);
                }
            }
        }
    }

    // | P2 | GET | /api/products/{id} | user | 200, 401, 403, 404 | 1.6 | planned |
    [GeneratedRegex(@"^\|\s*[A-Z]\d+\s*\|\s*(?<method>GET|POST|PUT|DELETE|PATCH)\s*\|\s*(?<path>/\S+)\s*\|[^|]*\|\s*(?<statuses>[0-9,\s]+)\|[^|]*\|\s*(?<state>[a-z]+)\s*\|\s*$")]
    private static partial Regex IndexRow();

    private sealed record ContractRow(string Operation, string[] Statuses, string State);
}
