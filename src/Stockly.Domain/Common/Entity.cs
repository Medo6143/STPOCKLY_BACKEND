using UUIDNext;

namespace Stockly.Domain.Common;

/// <summary>
/// Base for all entities. The id is a UUIDv7 assigned at construction (ADR-0009), so an aggregate can be
/// linked to other new rows inside one transaction before it is saved. Equality is by type and id.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity()
    {
        Id = Uuid.NewDatabaseFriendly(Database.PostgreSql);
    }

    public Guid Id { get; }

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);

    public bool Equals(Entity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
