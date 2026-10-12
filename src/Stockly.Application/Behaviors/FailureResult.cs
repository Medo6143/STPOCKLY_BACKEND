using System.Collections.Concurrent;
using System.Reflection;
using Stockly.Domain.Common;

namespace Stockly.Application.Behaviors;

/// <summary>Builds a failed <see cref="Result"/> or <see cref="Result{T}"/> for a pipeline's response type.</summary>
internal static class FailureResult
{
    private static readonly MethodInfo GenericFailure =
        typeof(Result).GetMethod(nameof(Result.Failure), 1, [typeof(Error)])
        ?? throw new InvalidOperationException("Result.Failure<T>(Error) not found.");

    private static readonly ConcurrentDictionary<Type, Func<Error, Result>> Factories = new();

    public static TResponse Create<TResponse>(Error error)
        where TResponse : Result
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)Result.Failure(error);
        }

        return (TResponse)Factories.GetOrAdd(typeof(TResponse), BuildFactory)(error);
    }

    private static Func<Error, Result> BuildFactory(Type responseType)
    {
        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            throw new InvalidOperationException($"{responseType.Name} is not Result or Result<T>.");
        }

        var failure = GenericFailure.MakeGenericMethod(responseType.GetGenericArguments()[0]);
        return error => (Result)failure.Invoke(null, [error])!;
    }
}
