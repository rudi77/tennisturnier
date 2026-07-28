using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Clubs;

/// <summary>
/// Wöchentlich wiederkehrende Öffnungszeit eines Platzes, in <em>lokaler</em>
/// Vereinszeit. Ohne die Zeitzone des Vereins ist daraus kein Punkt auf der
/// absoluten Zeitachse abzuleiten — dafür ist <see cref="CourtCalendar"/> zuständig.
/// </summary>
public sealed class AvailabilityWindow : Entity
{
    internal AvailabilityWindow(
        Guid id,
        Guid courtId,
        DayOfWeek dayOfWeek,
        TimeOnly opensAt,
        TimeOnly closesAt,
        DateOnly validFrom,
        DateOnly? validUntil)
        : base(id)
    {
        if (closesAt <= opensAt)
        {
            throw new DomainException(
                $"Die Öffnungszeit muss vorwärts laufen, war aber {opensAt:HH\\:mm} bis {closesAt:HH\\:mm}. " +
                "Über Mitternacht laufende Zeiten sind als zwei Regeln anzulegen.");
        }

        if (validUntil is not null && validUntil < validFrom)
        {
            throw new DomainException("Das Ende des Gültigkeitszeitraums liegt vor seinem Beginn.");
        }

        CourtId = courtId;
        DayOfWeek = dayOfWeek;
        OpensAt = opensAt;
        ClosesAt = closesAt;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private AvailabilityWindow(Guid id) : base(id)
    {
    }

    public Guid CourtId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly OpensAt { get; private set; }

    public TimeOnly ClosesAt { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    /// <summary>Offen, wenn die Regel bis auf Weiteres gilt (Sommer-/Winterbetrieb).</summary>
    public DateOnly? ValidUntil { get; private set; }

    public bool AppliesOn(DateOnly date) =>
        date.DayOfWeek == DayOfWeek
        && date >= ValidFrom
        && (ValidUntil is null || date <= ValidUntil);

    /// <summary>
    /// Zwei Regeln kollidieren, wenn sie am selben Wochentag in überlappenden
    /// Gültigkeitszeiträumen auch überlappende Uhrzeiten abdecken. Getrennte
    /// Sommer- und Winterregeln für denselben Wochentag sind dagegen zulässig.
    /// </summary>
    public bool ConflictsWith(AvailabilityWindow other) =>
        DayOfWeek == other.DayOfWeek
        && ValidityOverlaps(other)
        && OpensAt < other.ClosesAt
        && other.OpensAt < ClosesAt;

    private bool ValidityOverlaps(AvailabilityWindow other) =>
        (ValidUntil is null || other.ValidFrom <= ValidUntil)
        && (other.ValidUntil is null || ValidFrom <= other.ValidUntil);

    public override string ToString() =>
        $"{DayOfWeek} {OpensAt:HH\\:mm}–{ClosesAt:HH\\:mm} ab {ValidFrom:yyyy-MM-dd}";
}
