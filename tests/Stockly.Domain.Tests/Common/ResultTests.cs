using Stockly.Domain.Common;

namespace Stockly.Domain.Tests.Common;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.Validation("Sample.Invalid", "Sample is invalid.");

    [Fact]
    public void Success_NoValue_IsSuccess()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Failure_WithError_ExposesError()
    {
        var result = Result.Failure(SampleError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SampleError);
    }

    [Fact]
    public void Error_OnSuccess_Throws()
    {
        var result = Result.Success();

        var act = () => result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SuccessOfT_WithValue_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void FailureOfT_WithError_ValueThrows()
    {
        var result = Result.Failure<int>(SampleError);

        var act = () => result.Value;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SampleError);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitConversion_FromValueAndError_CreatesMatchingResults()
    {
        Result<string> success = "value";
        Result<string> failure = SampleError;
        Result plainFailure = SampleError;

        success.Value.Should().Be("value");
        failure.Error.Should().Be(SampleError);
        plainFailure.Error.Should().Be(SampleError);
    }

    [Fact]
    public void ErrorFactories_SetMatchingType()
    {
        Error.Validation("a", "b").Type.Should().Be(ErrorType.Validation);
        Error.NotFound("a", "b").Type.Should().Be(ErrorType.NotFound);
        Error.Conflict("a", "b").Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void ErrorType_Values_AreExplicitAndStable()
    {
        ((int)ErrorType.Validation).Should().Be(1);
        ((int)ErrorType.NotFound).Should().Be(2);
        ((int)ErrorType.Conflict).Should().Be(3);
        ((int)ErrorType.Forbidden).Should().Be(4);
        ((int)ErrorType.Unauthorized).Should().Be(5);
        ((int)ErrorType.Failure).Should().Be(6);
        ((int)ErrorType.Locked).Should().Be(7);
        ((int)ErrorType.TooManyRequests).Should().Be(8);
    }
}
