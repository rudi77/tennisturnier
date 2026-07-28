using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Security;

public enum ScopeType
{
    /// <summary>Systemweit, ohne Bezug auf eine einzelne Ressource.</summary>
    Global,

    Club,

    Tournament,
}

/// <summary>
/// Die Ressource, auf die sich eine Rolle oder eine Zugriffsprüfung bezieht.
///
/// Der springende Punkt aus ADR-0004: Rollen sind nicht global. „ClubAdmin" ohne
/// Verein ist keine sinnvolle Aussage — deshalb trägt jede Zuweisung ihren Scope.
/// </summary>
public readonly record struct ResourceScope
{
    private ResourceScope(ScopeType type, Guid? resourceId)
    {
        Type = type;
        ResourceId = resourceId;
    }

    public ScopeType Type { get; }

    /// <summary>Leer genau dann, wenn <see cref="Type"/> gleich <see cref="ScopeType.Global"/> ist.</summary>
    public Guid? ResourceId { get; }

    public static ResourceScope Global { get; } = new(ScopeType.Global, null);

    public static ResourceScope Club(Guid clubId) => Create(ScopeType.Club, clubId);

    public static ResourceScope Tournament(Guid tournamentId) => Create(ScopeType.Tournament, tournamentId);

    public static ResourceScope Create(ScopeType type, Guid? resourceId)
    {
        if (type == ScopeType.Global)
        {
            return resourceId is null || resourceId == Guid.Empty
                ? Global
                : throw new DomainException("Ein globaler Scope darf keine Ressource benennen.");
        }

        return resourceId is null || resourceId == Guid.Empty
            ? throw new DomainException($"Ein Scope vom Typ {type} braucht eine Ressource.")
            : new ResourceScope(type, resourceId);
    }

    public override string ToString() =>
        Type == ScopeType.Global ? "Global" : $"{Type}:{ResourceId}";
}
