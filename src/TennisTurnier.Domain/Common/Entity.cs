namespace TennisTurnier.Domain.Common;

/// <summary>
/// Basis aller Entitäten: Identität über <see cref="Id"/>, nicht über Werte.
/// </summary>
public abstract class Entity
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Eine Entität braucht eine Id.");
        }

        Id = id;
    }

    public Guid Id { get; private set; }

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
