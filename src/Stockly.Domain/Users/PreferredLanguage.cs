namespace Stockly.Domain.Users;

/// <summary>A user's app language. Stored as smallint: values are never reordered, only appended.</summary>
public enum PreferredLanguage
{
    Ar = 1,
    En = 2,
}
