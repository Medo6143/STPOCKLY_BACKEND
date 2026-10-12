using FluentValidation;
using NSubstitute;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Behaviors;
using Stockly.Application.Validation;
using Stockly.Domain.Common;

namespace Stockly.Application.Tests.Behaviors;

public sealed class PipelineBehaviorTests
{
    [Fact]
    public async Task Validation_Failures_ReturnValidationFailedWithCamelCaseFieldsAndSkipTheHandler()
    {
        var behavior = new ValidationBehavior<SampleCommand, Result<int>>([new SampleValidator()]);
        var called = false;

        var result = await behavior.Handle(
            new SampleCommand("", new SampleChild(0)),
            _ =>
            {
                called = true;
                return Task.FromResult<Result<int>>(1);
            },
            CancellationToken.None);

        called.Should().BeFalse();
        result.Error.Code.Should().Be(ValidationFailure.Code);
        result.Error.FieldErrors!.Keys.Should().BeEquivalentTo("name", "child.quantity");
    }

    [Fact]
    public async Task Validation_NonGenericResult_IsAlsoAFailure()
    {
        var behavior = new ValidationBehavior<SampleVoidCommand, Result>([new SampleVoidValidator()]);

        var result = await behavior.Handle(new SampleVoidCommand(""), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        result.Error.Code.Should().Be(ValidationFailure.Code);
    }

    [Fact]
    public async Task Validation_Valid_CallsTheHandler()
    {
        var behavior = new ValidationBehavior<SampleCommand, Result<int>>([new SampleValidator()]);

        var result = await behavior.Handle(new SampleCommand("ok", new SampleChild(1)), _ => Task.FromResult<Result<int>>(42), CancellationToken.None);

        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task Transaction_Success_Commits()
    {
        var (unitOfWork, decide) = CapturingUnitOfWork<Result<int>>();
        var behavior = new TransactionBehavior<SampleCommand, Result<int>>(unitOfWork);

        var result = await behavior.Handle(new SampleCommand("ok", new SampleChild(1)), _ => Task.FromResult<Result<int>>(1), CancellationToken.None);

        decide()(result).Should().BeTrue();
    }

    [Fact]
    public async Task Transaction_Failure_RollsBack()
    {
        var (unitOfWork, decide) = CapturingUnitOfWork<Result<int>>();
        var behavior = new TransactionBehavior<SampleCommand, Result<int>>(unitOfWork);

        var result = await behavior.Handle(
            new SampleCommand("ok", new SampleChild(1)), _ => Task.FromResult<Result<int>>(Error.Conflict("X.Y", "m")), CancellationToken.None);

        decide()(result).Should().BeFalse();
    }

    [Fact]
    public async Task Transaction_FailureOfCommitOnFailureCommand_Commits()
    {
        var (unitOfWork, decide) = CapturingUnitOfWork<Result>();
        var behavior = new TransactionBehavior<SampleCommitOnFailureCommand, Result>(unitOfWork);

        var result = await behavior.Handle(
            new SampleCommitOnFailureCommand(), _ => Task.FromResult(Result.Failure(Error.Unauthorized("X.Y", "m"))), CancellationToken.None);

        decide()(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("Email", "email")]
    [InlineData("Shop.Name", "shop.name")]
    [InlineData("Items[2].Quantity", "items[2].quantity")]
    [InlineData("already.camel", "already.camel")]
    public void FieldPath_ToCamelCase_MatchesTheContract(string path, string expected)
    {
        FieldPath.ToCamelCase(path).Should().Be(expected);
    }

    private static (IUnitOfWork UnitOfWork, Func<Func<T, bool>> Decide) CapturingUnitOfWork<T>()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        Func<T, bool>? shouldCommit = null;
        unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<T>>>(), Arg.Any<Func<T, bool>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                shouldCommit = call.ArgAt<Func<T, bool>>(1);
                return call.ArgAt<Func<CancellationToken, Task<T>>>(0)(CancellationToken.None);
            });
        return (unitOfWork, () => shouldCommit!);
    }

    public sealed record SampleCommand(string Name, SampleChild Child) : ICommand<int>;

    public sealed record SampleChild(int Quantity);

    public sealed record SampleVoidCommand(string Name) : ICommand;

    public sealed record SampleCommitOnFailureCommand : ICommand, ICommitOnFailure;

    private sealed class SampleValidator : AbstractValidator<SampleCommand>
    {
        public SampleValidator()
        {
            RuleFor(c => c.Name).NotEmpty();
            RuleFor(c => c.Child.Quantity).GreaterThan(0);
        }
    }

    private sealed class SampleVoidValidator : AbstractValidator<SampleVoidCommand>
    {
        public SampleVoidValidator()
        {
            RuleFor(c => c.Name).NotEmpty();
        }
    }
}
