namespace Stockly.Infrastructure.Persistence;

/// <summary>
/// A write would cross a tenant boundary (ADR-0002). This is a programming error, never an expected failure,
/// so it is an exception and not a <c>Result</c>.
/// </summary>
public sealed class TenantViolationException : InvalidOperationException
{
    public TenantViolationException()
    {
    }

    public TenantViolationException(string message)
        : base(message)
    {
    }

    public TenantViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
