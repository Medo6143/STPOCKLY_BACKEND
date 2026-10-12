using Npgsql;
using Stockly.Domain.Shops;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>erd.md §3.1 constraints of the Identity migration, checked with raw SQL (the Domain is bypassed on purpose).</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class IdentityConstraintTests(PostgresFixture fixture)
{
    private const string InsertMember =
        "INSERT INTO shop_members (shop_id, user_id, role, is_active, joined_at) VALUES (@p0, @p1, @p2, true, now())";

    private const string InsertToken =
        "INSERT INTO refresh_tokens (id, user_id, token_hash, family_id, expires_at, created_at) VALUES (@p0, @p1, @p2, @p0, now(), now())";

    private const string InsertSubscription =
        "INSERT INTO shop_subscriptions (id, shop_id, plan_id, status, starts_on, created_at) VALUES (@p0, @p1, @p2, @p3, current_date, now())";

    [Fact]
    public async Task ShopMembers_SecondOwner_Rejected()
    {
        var shop = await fixture.CreateShopAsync();
        await fixture.ExecuteAsync(InsertMember, shop.Id, await fixture.CreateUserAsync(), (short)ShopRole.Owner);
        var secondUser = await fixture.CreateUserAsync();

        var act = () => fixture.ExecuteAsync(InsertMember, shop.Id, secondUser, (short)ShopRole.Owner);

        await ShouldViolate(act, PostgresErrorCodes.UniqueViolation, "ux_shop_members_shop_id_owner");
    }

    [Fact]
    public async Task ShopMembers_OwnerAndStaff_Allowed()
    {
        var shop = await fixture.CreateShopAsync();
        await fixture.ExecuteAsync(InsertMember, shop.Id, await fixture.CreateUserAsync(), (short)ShopRole.Owner);
        var staff = await fixture.CreateUserAsync();

        var act = () => fixture.ExecuteAsync(InsertMember, shop.Id, staff, (short)ShopRole.Staff);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ShopMembers_SameUserTwiceInShop_RejectedByPrimaryKey()
    {
        var shop = await fixture.CreateShopAsync();
        var user = await fixture.CreateUserAsync();
        await fixture.ExecuteAsync(InsertMember, shop.Id, user, (short)ShopRole.Staff);

        var act = () => fixture.ExecuteAsync(InsertMember, shop.Id, user, (short)ShopRole.Staff);

        await ShouldViolate(act, PostgresErrorCodes.UniqueViolation, "pk_shop_members");
    }

    [Fact]
    public async Task RefreshTokens_DuplicateHash_Rejected()
    {
        var user = await fixture.CreateUserAsync();
        var hash = Guid.NewGuid().ToString("N");
        await fixture.ExecuteAsync(InsertToken, Guid.CreateVersion7(), user, hash);

        var act = () => fixture.ExecuteAsync(InsertToken, Guid.CreateVersion7(), user, hash);

        await ShouldViolate(act, PostgresErrorCodes.UniqueViolation, "ux_refresh_tokens_token_hash");
    }

    [Fact]
    public async Task Users_DuplicateNormalizedEmail_Rejected()
    {
        var user = await fixture.CreateUserAsync();
        var email = await fixture.ScalarAsync<string>("SELECT normalized_email FROM users WHERE id = @p0", user);

        var act = () => fixture.ExecuteAsync(
            "INSERT INTO users (id, full_name, preferred_language, platform_role, created_at, user_name, normalized_user_name, " +
            "email, normalized_email, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) " +
            "VALUES (@p0, 'x', 1, 0, now(), 'other', 'OTHER', 'x', @p1, false, false, false, false, 0)",
            Guid.CreateVersion7(),
            email!);

        await ShouldViolate(act, PostgresErrorCodes.UniqueViolation, "ux_users_normalized_email");
    }

    [Fact]
    public async Task ShopSubscriptions_SecondActive_Rejected_EndedAllowed()
    {
        var shop = await fixture.CreateShopAsync();
        await fixture.ExecuteAsync(InsertSubscription, Guid.CreateVersion7(), shop.Id, DefaultPlan.Id, (short)SubscriptionStatus.Active);

        var ended = () => fixture.ExecuteAsync(InsertSubscription, Guid.CreateVersion7(), shop.Id, DefaultPlan.Id, (short)SubscriptionStatus.Ended);
        var secondActive = () => fixture.ExecuteAsync(InsertSubscription, Guid.CreateVersion7(), shop.Id, DefaultPlan.Id, (short)SubscriptionStatus.Active);

        await ended.Should().NotThrowAsync();
        await ShouldViolate(secondActive, PostgresErrorCodes.UniqueViolation, "ux_shop_subscriptions_shop_id_active");
    }

    [Fact]
    public async Task Plans_DuplicateCode_Rejected()
    {
        var act = () => fixture.ExecuteAsync(
            "INSERT INTO plans (id, code, name_ar, name_en, daily_scan_quota, max_members, monthly_price, is_active) " +
            "VALUES (@p0, @p1, 'أ', 'A', 1, 1, 0, true)",
            Guid.CreateVersion7(),
            DefaultPlan.Code);

        await ShouldViolate(act, PostgresErrorCodes.UniqueViolation, "ux_plans_code");
    }

    [Fact]
    public async Task StockMovements_UnknownCreatedByUser_RejectedByForeignKey()
    {
        var shop = await fixture.CreateShopAsync();
        var product = Catalog.NewProduct(shop.Id);
        await using (var context = fixture.CreateContext(shop.Id))
        {
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        var act = () => fixture.ExecuteAsync(
            "INSERT INTO stock_movements (id, shop_id, product_id, quantity_change, balance_after, reason, created_by_user_id, created_at) " +
            "VALUES (@p0, @p1, @p2, 1, 1, 4, @p3, now())",
            Guid.CreateVersion7(),
            shop.Id,
            product.Id,
            Guid.CreateVersion7());

        await ShouldViolate(act, PostgresErrorCodes.ForeignKeyViolation, "fk_stock_movements_users_created_by_user_id");
    }

    private static async Task ShouldViolate(Func<Task> act, string sqlState, string constraint)
    {
        var error = (await act.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(sqlState);
        error.ConstraintName.Should().Be(constraint);
    }
}
