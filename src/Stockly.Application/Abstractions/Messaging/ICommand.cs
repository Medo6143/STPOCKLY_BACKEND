using MediatR;
using Stockly.Domain.Common;

namespace Stockly.Application.Abstractions.Messaging;

/// <summary>A use case that writes. Runs in one database transaction (<c>TransactionBehavior</c>).</summary>
public interface ICommand : IRequest<Result>, ITransactionalRequest;

/// <summary>A use case that writes and returns a value. Runs in one database transaction (<c>TransactionBehavior</c>).</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, ITransactionalRequest;

/// <summary>A read-only use case. No transaction.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Marks requests that <c>TransactionBehavior</c> wraps in a transaction.</summary>
public interface ITransactionalRequest;

/// <summary>
/// A command whose writes on a failure path are deliberate and must be committed, e.g. revoking a stolen refresh
/// token family before returning <c>Auth.RefreshTokenReused</c>. Other commands roll back on any failure.
/// </summary>
public interface ICommitOnFailure;
