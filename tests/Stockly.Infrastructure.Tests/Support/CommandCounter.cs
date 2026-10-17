using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Stockly.Infrastructure.Tests.Support;

/// <summary>
/// Counts the SQL commands one <see cref="DbContext"/> executes, through EF Core's diagnostic events (rule 33 tests: "one
/// query", "no N+1"). Commands of other contexts (tests running in parallel) are ignored.
/// </summary>
internal sealed class CommandCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly DbContext _context;
    private readonly IDisposable _allListeners;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<string> _commands = [];

    public CommandCounter(DbContext context)
    {
        _context = context;
        _allListeners = DiagnosticListener.AllListeners.Subscribe(this);
    }

    /// <summary>The SQL text of every command the context executed since this counter was created.</summary>
    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands)
            {
                return [.. _commands];
            }
        }
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == DbLoggerCategory.Name)
        {
            lock (_subscriptions)
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Key == RelationalEventId.CommandExecuting.Name
            && value.Value is CommandEventData data
            && ReferenceEquals(data.Context, _context))
        {
            lock (_commands)
            {
                _commands.Add(data.Command.CommandText);
            }
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        _allListeners.Dispose();
        lock (_subscriptions)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }
    }
}
