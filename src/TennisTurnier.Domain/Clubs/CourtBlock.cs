using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Clubs;

/// <summary>
/// Grund einer Platzsperre. Der Grund ist keine Kosmetik: die Turnierleitung
/// entscheidet anhand von ihm, ob eine Sperre für ein Finale verhandelbar ist.
/// </summary>
public enum BlockReason
{
    /// <summary>Vereinstraining.</summary>
    Training,

    /// <summary>Punktespiel der Vereinsmannschaft.</summary>
    LeagueMatch,

    /// <summary>Platzpflege, Reparatur, Umbau.</summary>
    Maintenance,

    /// <summary>Regen, Sturm, unbespielbarer Belag.</summary>
    Weather,

    Other,
}

/// <summary>
/// Einmalige Sperre eines Platzes auf der absoluten Zeitachse. Anders als
/// <see cref="AvailabilityWindow"/> ist eine Sperre datiert und braucht daher
/// keine Zeitzonenauflösung.
/// </summary>
public sealed class CourtBlock : Entity
{
    internal CourtBlock(Guid id, Guid courtId, TimeSlot period, BlockReason reason, string? note)
        : base(id)
    {
        CourtId = courtId;
        Period = period;
        Reason = reason;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private CourtBlock(Guid id) : base(id)
    {
    }

    public Guid CourtId { get; private set; }

    public TimeSlot Period { get; private set; }

    public BlockReason Reason { get; private set; }

    public string? Note { get; private set; }

    public override string ToString() => $"{Reason}: {Period}";
}
