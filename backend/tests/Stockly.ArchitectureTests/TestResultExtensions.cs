using NetArchTest.Rules;

namespace Stockly.ArchitectureTests;

internal static class TestResultExtensions
{
    /// <summary>Asserts the rule passed, listing the offending types when it did not.</summary>
    public static void ShouldBeSuccessful(this TestResult result, string because)
    {
        var failingTypes = result.FailingTypeNames ?? [];

        result.IsSuccessful.Should().BeTrue(
            "{0}. Offending types: {1}",
            because,
            string.Join(", ", failingTypes));
    }
}
