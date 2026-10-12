using Stockly.Domain.Common;

namespace Stockly.Application.Features.Auth;

/// <summary>Auth error codes of the API contract (§9). Codes are stable; the Api localizes title and detail.</summary>
public static class AuthErrors
{
    public static readonly Error EmailTaken =
        Error.Conflict("Auth.EmailTaken", "This email is already registered.");

    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "The email or password is incorrect.");

    public static readonly Error NoActiveShop =
        Error.Forbidden("Auth.NoActiveShop", "The user has no active shop membership.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("Auth.InvalidRefreshToken", "The refresh token is unknown or expired.");

    public static readonly Error RefreshTokenReused =
        Error.Unauthorized("Auth.RefreshTokenReused", "A revoked refresh token was reused; the session was revoked.");

    public static readonly Error Unauthenticated =
        Error.Unauthorized("Auth.Unauthenticated", "The access token is missing, invalid or expired.");

    public static readonly Error Forbidden =
        Error.Forbidden("Auth.Forbidden", "The user's role does not allow this action.");
}
