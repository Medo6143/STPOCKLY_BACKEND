using System.Diagnostics.CodeAnalysis;

namespace Stockly.Admin.Services;

/// <summary>The outcome of one API call: a value, or the <see cref="ApiProblem"/> to show.</summary>
public sealed class ApiResult<T>
{
    internal ApiResult(T? value, ApiProblem? problem)
    {
        Value = value;
        Problem = problem;
    }

    public T? Value { get; }

    public ApiProblem? Problem { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Problem))]
    public bool IsSuccess => Problem is null;
}

/// <summary>Creates <see cref="ApiResult{T}"/> values.</summary>
public static class ApiResult
{
    public static ApiResult<T> Success<T>(T value) => new(value, null);

    public static ApiResult<T> Failure<T>(ApiProblem problem) => new(default, problem);
}

/// <summary>The success value of calls that answer <c>204 No Content</c>.</summary>
public readonly record struct NoContent;
