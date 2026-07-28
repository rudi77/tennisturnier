using System.Globalization;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Matches;

/// <summary>
/// Wer auf einer Seite eines Matches steht — auch dann, wenn es noch niemand ist.
///
/// Das ist die Abstraktion, die den ganzen Turnierbaum trägt (ADR-0001): der
/// Draw lässt sich vollständig aufbauen, bevor ein einziges Match gespielt ist.
/// „Sieger aus Match 7" und „Zweiter der Gruppe B" sind dabei derselbe
/// Mechanismus — eine Referenz, die aufgelöst wird, sobald ihr Vorgänger
/// entschieden ist. Der Übergang Gruppenphase → Endrunde ist damit kein
/// Sonderfall gegenüber Viertelfinale → Halbfinale.
/// </summary>
public abstract record ParticipantRef
{
    private ParticipantRef()
    {
    }

    /// <summary>Steht bereits fest.</summary>
    public sealed record Entry(Guid EntryId) : ParticipantRef
    {
        public override string ToString() => $"Meldung {EntryId}";
    }

    /// <summary>Wird zum Sieger des genannten Matches.</summary>
    public sealed record WinnerOf(Guid MatchId) : ParticipantRef
    {
        public override string ToString() => $"Sieger aus {MatchId}";
    }

    /// <summary>Wird zum Verlierer des genannten Matches — für Trostrunde und Spiel um Platz 3.</summary>
    public sealed record LoserOf(Guid MatchId) : ParticipantRef
    {
        public override string ToString() => $"Verlierer aus {MatchId}";
    }

    /// <summary>Wird zu dem, der die genannte Position in einer Gruppe belegt.</summary>
    public sealed record GroupPosition(Guid PhaseId, string Group, int Rank) : ParticipantRef
    {
        public override string ToString() => $"{Group}{Rank}";
    }

    /// <summary>
    /// Freilos. Der Gegner kommt kampflos weiter — ein Match mit einem Freilos
    /// wird nie gespielt, sondern beim Aufbau des Baums entschieden.
    /// </summary>
    public sealed record Bye : ParticipantRef
    {
        public override string ToString() => "Freilos";
    }

    /// <summary>
    /// Position im Baum, die kein Vorgänger füllt und die auch kein Freilos ist.
    /// Kommt in einer Phase vor, deren Teilnehmer noch nicht feststehen.
    /// </summary>
    public sealed record Unassigned : ParticipantRef
    {
        public override string ToString() => "offen";
    }

    /// <summary>Steht der Teilnehmer fest?</summary>
    public bool IsResolved => this is Entry or Bye;

    /// <summary>
    /// Die Meldung, falls sie feststeht. Ein Freilos hat bewusst keine — es ist
    /// kein Teilnehmer, sondern seine Abwesenheit.
    /// </summary>
    public Guid? ResolvedEntryId => this is Entry entry ? entry.EntryId : null;

    /// <summary>
    /// Das Match, dessen Ausgang diese Referenz auflöst. Leer, wenn sie nicht
    /// von einem Match abhängt.
    /// </summary>
    public Guid? DependsOnMatch => this switch
    {
        WinnerOf winner => winner.MatchId,
        LoserOf loser => loser.MatchId,
        _ => null,
    };

    public static ParticipantRef Of(Guid entryId) =>
        entryId == Guid.Empty
            ? throw new DomainException("Eine Meldungsreferenz braucht eine Meldung.")
            : new Entry(entryId);

    public static ParticipantRef FromWinnerOf(Guid matchId) =>
        matchId == Guid.Empty
            ? throw new DomainException("Eine Siegerreferenz braucht ein Match.")
            : new WinnerOf(matchId);

    public static ParticipantRef FromLoserOf(Guid matchId) =>
        matchId == Guid.Empty
            ? throw new DomainException("Eine Verliererreferenz braucht ein Match.")
            : new LoserOf(matchId);

    public static ParticipantRef FromGroupPosition(Guid phaseId, string group, int rank)
    {
        if (phaseId == Guid.Empty)
        {
            throw new DomainException("Eine Gruppenplatzreferenz braucht eine Phase.");
        }

        if (string.IsNullOrWhiteSpace(group))
        {
            throw new DomainException("Eine Gruppenplatzreferenz braucht eine Gruppe.");
        }

        return rank < 1
            ? throw new DomainException($"Ein Gruppenplatz beginnt bei 1, war {rank}.")
            : new GroupPosition(phaseId, group.Trim(), rank);
    }

    public static ParticipantRef ByeSlot { get; } = new Bye();

    public static ParticipantRef Open { get; } = new Unassigned();

    /// <summary>
    /// Kompakte Textdarstellung für die Ablage.
    ///
    /// Sie steht hier und nicht im Persistenzadapter, weil sie zusammen mit
    /// <see cref="Parse"/> ein Paar bildet: eine Kodierung, deren beide Hälften
    /// auseinanderlaufen können, ist der sichere Weg zu unlesbaren Altdaten.
    /// Der Domänenkern bleibt dabei frei von Infrastruktur — es ist reine
    /// Zeichenkettenarbeit.
    /// </summary>
    public string Encode() => this switch
    {
        Entry entry => $"E:{entry.EntryId:D}",
        WinnerOf winner => $"W:{winner.MatchId:D}",
        LoserOf loser => $"L:{loser.MatchId:D}",
        GroupPosition position => $"G:{position.PhaseId:D}:{position.Rank}:{position.Group}",
        Bye => "BYE",
        _ => "OPEN",
    };

    public static ParticipantRef Parse(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new DomainException("Eine leere Teilnehmerreferenz lässt sich nicht lesen.");
        }

        if (encoded == "BYE")
        {
            return ByeSlot;
        }

        if (encoded == "OPEN")
        {
            return Open;
        }

        // Die Gruppe darf Doppelpunkte enthalten und steht deshalb zuletzt.
        var parts = encoded.Split(':', 4);

        return parts switch
        {
            ["E", var id] => Of(Guid.Parse(id)),
            ["W", var id] => FromWinnerOf(Guid.Parse(id)),
            ["L", var id] => FromLoserOf(Guid.Parse(id)),
            ["G", var phaseId, var rank, var group] =>
                FromGroupPosition(Guid.Parse(phaseId), group, int.Parse(rank, CultureInfo.InvariantCulture)),
            _ => throw new DomainException($"Unlesbare Teilnehmerreferenz „{encoded}“."),
        };
    }
}
