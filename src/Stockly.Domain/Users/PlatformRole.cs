namespace Stockly.Domain.Users;

/// <summary>Platform-wide role (docs/erd.md §3.1). Stored as smallint; 0 is the default for shop users.</summary>
public enum PlatformRole
{
    None = 0,
    PlatformAdmin = 1,
}
