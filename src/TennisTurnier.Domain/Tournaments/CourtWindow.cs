using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Ein Zeitfenster, in dem ein Platz für dieses Turnier zur Verfügung steht —
/// „Platz 3 am 16. Mai von 9 bis 18 Uhr".
///
/// Das ist ausdrücklich <em>keine</em> wöchentliche Öffnungszeitregel. Die
/// Reservierung findet außerhalb dieser Anwendung statt: der Veranstalter
/// vereinbart am Telefon konkrete Stunden an konkreten Tagen, und genau das
/// wird hier festgehalten. Ein Wochentagsraster wäre Vereinsstammdaten und
/// verlangte, den Verein zu pflegen, bevor man ein Turnier ausschreiben kann.
///
/// Damit fällt auch die Unterscheidung zwischen Öffnungszeit und Sperre weg:
/// eine Stunde, die nicht reserviert ist, wird schlicht nicht angelegt. Das
/// Fenster <em>ist</em> die Verfügbarkeit, es gibt nichts mehr abzuziehen.
/// </summary>
public sealed class CourtWindow : Entity
{
    internal CourtWindow(Guid id, Guid tournamentId, Guid courtId, TimeSlot period)
        : base(id)
    {
        if (tournamentId == Guid.Empty || courtId == Guid.Empty)
        {
            throw new DomainException("Ein Platzzeitfenster braucht Turnier und Platz.");
        }

        TournamentId = tournamentId;
        CourtId = courtId;
        Period = period;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private CourtWindow(Guid id) : base(id)
    {
    }

    /// <summary>
    /// Das Turnier, bewusst auch hier hinterlegt und nicht nur über den Platz
    /// erreichbar.
    ///
    /// Der Query-Filter aus ADR-0004 arbeitet auf der Menge der sichtbaren
    /// Turniere. Ginge er von hier über den Platz zum Turnier, wäre er
    /// zweistufig — dieselbe Kette, die Phase, Match und Platzzuweisung bereits
    /// mit einer eigenen Turnierkennung vermeiden.
    /// </summary>
    public Guid TournamentId { get; private set; }

    public Guid CourtId { get; private set; }

    public TimeSlot Period { get; private set; }

    /// <summary>
    /// Zwei Fenster desselben Platzes dürfen sich nicht überschneiden. Täten sie
    /// es, zählte dieselbe Stunde zweimal, und der Spielplan hielte einen Platz
    /// für doppelt vorhanden.
    /// </summary>
    public bool ConflictsWith(CourtWindow other) =>
        CourtId == other.CourtId && Period.Overlaps(other.Period);

    public override string ToString() => Period.ToString();
}
