using System.Reflection;
using Stockly.Domain.Common;

namespace Stockly.Domain.Tests.Common;

public sealed class EntityTests
{
    [Fact]
    public void Constructor_NewEntity_AssignsVersion7Id()
    {
        var entity = new FirstEntity();

        entity.Id.Should().NotBe(Guid.Empty);
        entity.Id.Version.Should().Be(7, "ids are UUIDv7 (ADR-0010)");
        entity.Id.ToString()[14].Should().Be('7');
        (entity.Id.Variant >> 2).Should().Be(0b10, "RFC 9562: the top two bits of the variant nibble are 10");
    }

    [Fact]
    public void Constructor_ManyEntities_AssignsUniqueIds()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => new FirstEntity().Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Constructor_SequentialEntities_EmbeddedTimestampsNeverDecrease()
    {
        var timestamps = Enumerable.Range(0, 1000).Select(_ => UnixMilliseconds(new FirstEntity().Id)).ToList();

        timestamps.Should().BeInAscendingOrder("a UUIDv7 starts with its creation time in Unix milliseconds");
    }

    [Fact]
    public void Constructor_EntitiesInDifferentMilliseconds_IdsSortInCreationOrder()
    {
        // Guid.CreateVersion7 orders ids by millisecond; inside one millisecond the rest is random (ADR-0010).
        var ids = new List<Guid> { new FirstEntity().Id };
        while (ids.Count < 20)
        {
            var next = new FirstEntity().Id;
            if (UnixMilliseconds(next) > UnixMilliseconds(ids[^1]))
            {
                ids.Add(next);
            }
        }

        ids.Select(id => id.ToString()).Should().BeInAscendingOrder(StringComparer.Ordinal, "PostgreSQL sorts uuid by bytes");
    }

    [Fact]
    public void Equals_SameTypeAndSameId_ReturnsTrue()
    {
        var first = new FirstEntity();
        var second = WithId(new FirstEntity(), first.Id);

        first.Equals(second).Should().BeTrue();
        (first == second).Should().BeTrue();
        (first != second).Should().BeFalse();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Equals_SameTypeDifferentId_ReturnsFalse()
    {
        var first = new FirstEntity();
        var second = new FirstEntity();

        first.Equals(second).Should().BeFalse();
        (first == second).Should().BeFalse();
        (first != second).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentTypeSameId_ReturnsFalse()
    {
        var first = new FirstEntity();
        var other = WithId(new SecondEntity(), first.Id);

        first.Equals(other).Should().BeFalse();
        first.Equals((object)other).Should().BeFalse();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var entity = new FirstEntity();
        Entity? missing = null;

        entity.Equals(missing).Should().BeFalse();
        new FirstEntity().Equals((object?)null).Should().BeFalse();
        (new FirstEntity() == missing).Should().BeFalse();
        (missing == new FirstEntity()).Should().BeFalse();
    }

    [Fact]
    public void EqualityOperator_BothNull_ReturnsTrue()
    {
        FirstEntity? left = null;
        FirstEntity? right = null;

        (left == right).Should().BeTrue();
    }

    [Fact]
    public void Equals_SameInstanceAsObject_ReturnsTrue()
    {
        var entity = new FirstEntity();

        entity.Equals((object)entity).Should().BeTrue();
    }

    private static long UnixMilliseconds(Guid id) =>
        Convert.ToInt64(id.ToString("N")[..12], 16);

    // Simulates EF materialization, which sets the Id through its backing field.
    private static T WithId<T>(T entity, Guid id)
        where T : Entity
    {
        var field = typeof(Entity).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Entity.Id backing field not found");
        field.SetValue(entity, id);
        return entity;
    }

    private sealed class FirstEntity : Entity;

    private sealed class SecondEntity : Entity;
}
