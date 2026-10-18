using Microsoft.AspNetCore.Components;

namespace Stockly.Admin.Components;

/// <summary>
/// Base of the data pages: a cancellation token that is cancelled when the page is left, so a slow response never updates
/// a page that is gone (AGENTS.md rule 29).
/// </summary>
public abstract class ApiPageBase : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    protected CancellationToken PageToken => _lifetime.Token;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
