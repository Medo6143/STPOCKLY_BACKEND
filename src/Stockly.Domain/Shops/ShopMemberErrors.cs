using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

public static class ShopMemberErrors
{
    public static readonly Error ShopRequired =
        Error.Validation("ShopMember.ShopRequired", "A membership must belong to a shop.");

    public static readonly Error UserRequired =
        Error.Validation("ShopMember.UserRequired", "A membership must belong to a user.");

    public static readonly Error UnknownRole =
        Error.Validation("ShopMember.UnknownRole", "The member role is not supported.");
}
