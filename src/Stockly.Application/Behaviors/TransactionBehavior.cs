using MediatR;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Common;

namespace Stockly.Application.Behaviors;

/// <summary>
/// Runs a command in one database transaction. A successful result commits; a failed result rolls back unless the
/// command is an <see cref="ICommitOnFailure"/>; an exception always rolls back. Queries are not wrapped.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITransactionalRequest
    where TResponse : Result
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var commitOnFailure = request is ICommitOnFailure;
        return unitOfWork.ExecuteInTransactionAsync(
            token => next(token),
            response => response.IsSuccess || commitOnFailure,
            cancellationToken);
    }
}
