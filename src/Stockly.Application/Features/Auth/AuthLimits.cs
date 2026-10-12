namespace Stockly.Application.Features.Auth;

/// <summary>Field limits from the API contract (A1–A4).</summary>
public static class AuthLimits
{
    public const int EmailMaxLength = 256;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int FullNameMaxLength = 200;
    public const int PhoneMaxLength = 30;
    public const int DeviceIdMaxLength = 100;
    public const int ShopNameMaxLength = 200;

    /// <summary>A refresh token is 43 base64url characters; anything far longer is not one of ours.</summary>
    public const int RefreshTokenMaxLength = 200;
}
