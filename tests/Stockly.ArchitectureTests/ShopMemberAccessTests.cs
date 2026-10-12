using System.Text.RegularExpressions;
using Stockly.Infrastructure.Persistence;

namespace Stockly.ArchitectureTests;

/// <summary>
/// ADR-0002 (update 2026-10-12): <c>ShopMember</c> is not a tenant entity, so the condition for that is enforced by
/// construction: only <c>ShopMembershipRepository</c> touches its set (every method takes a user id), and only it and
/// the mapping/migrations mention the <c>shop_members</c> table.
/// </summary>
public sealed partial class ShopMemberAccessTests
{
    private const string TableName = "shop_members";

    private static readonly string Repository =
        SourceFiles.PathOf(Layers.Infrastructure, "Persistence", "Repositories", "ShopMembershipRepository.cs");

    private static readonly string Configuration =
        SourceFiles.PathOf(Layers.Infrastructure, "Persistence", "Configurations", "ShopMemberConfiguration.cs");

    private static readonly string Migrations =
        SourceFiles.PathOf(Layers.Infrastructure, "Persistence", "Migrations") + Path.DirectorySeparatorChar;

    [Fact]
    public void SourceFiles_ShopMemberSetAccess_OnlyInTheMembershipRepository()
    {
        var files = SourceFiles.All();

        var users = files.Where(file => SetAccess().IsMatch(SourceFiles.Read(file))).ToList();

        files.Should().Contain(Repository, "the scan must see the repository, or this rule passes vacuously");
        users.Should().Equal([Repository], "every shop_members query must go through IShopMembershipRepository (filtered by user id)");
    }

    [Fact]
    public void SourceFiles_ShopMembersTable_OnlyInMappingRepositoryAndMigrations()
    {
        var offenders = SourceFiles.All()
            .Where(file => SourceFiles.Read(file).Contains(TableName, StringComparison.Ordinal))
            .Where(file => file != Configuration && file != Repository && !file.StartsWith(Migrations, StringComparison.Ordinal));

        offenders.Should().BeEmpty("raw SQL on shop_members would bypass the user-id condition");
    }

    [Fact]
    public void DbContext_HasNoShopMembersProperty()
    {
        typeof(StocklyDbContext).GetProperties()
            .Where(property => property.PropertyType.IsGenericType && property.PropertyType.GetGenericArguments()[0].Name == "ShopMember")
            .Should().BeEmpty("a public DbSet would let any handler query memberships without a user id");
    }

    [Fact]
    public void SetAccessPattern_MatchesEveryWayToReachTheSet()
    {
        SetAccess().IsMatch("db.Set<ShopMember>()").Should().BeTrue();
        SetAccess().IsMatch("db.Set< Domain.Shops.ShopMember >()").Should().BeTrue();
        SetAccess().IsMatch("public DbSet<ShopMember> Members").Should().BeTrue();
        SetAccess().IsMatch("context.ShopMembers.Where(...)").Should().BeTrue();
        SetAccess().IsMatch("IShopMembershipRepository memberships").Should().BeFalse();
    }

    // Set<ShopMember>, DbSet<ShopMember>, or a ShopMembers member.
    [GeneratedRegex(@"Set<\s*(?:[\w.]+\.)?ShopMember\s*>|\bShopMembers\b")]
    private static partial Regex SetAccess();
}
