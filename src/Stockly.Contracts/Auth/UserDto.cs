namespace Stockly.Contracts.Auth;

/// <summary><see cref="PreferredLanguage"/> is <c>ar</c> or <c>en</c>.</summary>
public sealed record UserDto(Guid Id, string Email, string FullName, string? Phone, string PreferredLanguage);
