using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Security;

public enum ScopeType
{
    /// <summary>Systemweit, ohne Bezug auf eine einzelne Ressource.</summary>
    Global,

    Tournament,
}

/// <summary>
/// Die Ressource, auf die sich eine Rolle oder eine Zugriffsprüfung bezieht.
///
/// Der springende Punkt aus ADR-0004: Rollen sind nicht global. „Turnierleiter"
/// ohne Turnier ist keine sinnvolle Aussage — deshalb trägt jede Zuweisung
/// ihren Scope.
///
/// Es gab hier einmal einen Scope <c>Club</c>. Er ist entfallen, weil der
/// Verein als Mandantengrenze entfällt; was von ihm blieb, ist das Turnier.
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

    public static ResourceScope Tournament(Guid tournamentId) => Create(ScopeType.Tournament, tournamentId);

    public static ResourceScope Create(ScopeType type, Guid? resourceId)
    {
        // Keine Ressource und die leere Guid sind dasselbe: beides heißt „nicht
        // angegeben". Zwei Schreibweisen für denselben Fall wären zwei Wege,
        // ihn zu übersehen.
        var resource = resourceId ?? Guid.Empty;

        if (type == ScopeType.Global)
        {
            if (resource != Guid.Empty)
            {
                throw new DomainException("Ein globaler Scope darf keine Ressource benennen.");
            }

            return Global;
        }

        if (resource == Guid.Empty)
        {
            throw new DomainException($"Ein Scope vom Typ {type} braucht eine Ressource.");
        }

        return new ResourceScope(type, resource);
    }

    public override string ToString() =>
        Type == ScopeType.Global ? "Global" : $"{Type}:{ResourceId}";
}
