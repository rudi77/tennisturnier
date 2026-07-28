using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Legt die Herkunft einer Match-Seite als kompakten Text ab.
///
/// Kodierung und Auswertung liegen bewusst im Domänentyp selbst
/// (<see cref="ParticipantRef.Encode"/> und <see cref="ParticipantRef.Parse"/>):
/// eine Kodierung, deren beide Hälften an verschiedenen Stellen gepflegt werden,
/// läuft irgendwann auseinander — und dann sind gespeicherte Turnierbäume
/// unlesbar.
/// </summary>
public sealed class ParticipantRefConverter : ValueConverter<ParticipantRef, string>
{
    public ParticipantRefConverter()
        : base(reference => reference.Encode(), encoded => ParticipantRef.Parse(encoded))
    {
    }
}

/// <summary>
/// ParticipantRef ist ein unveränderlicher Record mit Wertgleichheit; der
/// Vergleicher macht das für die Änderungsverfolgung nutzbar, statt Referenzen
/// zu vergleichen.
/// </summary>
public sealed class ParticipantRefComparer : ValueComparer<ParticipantRef>
{
    public ParticipantRefComparer()
        : base(
            (left, right) => left == right,
            reference => reference.GetHashCode(),
            reference => reference)
    {
    }
}
