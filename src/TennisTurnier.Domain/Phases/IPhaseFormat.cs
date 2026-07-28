using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// Eine Paarung, die eine Phase erzeugen will. Noch ohne Id — die vergibt die
/// Phase beim Anlegen des Matches.
/// </summary>
public sealed record Pairing(
    int Round,
    int Position,
    ParticipantRef Side1,
    ParticipantRef Side2,
    string? Label = null,
    string? Group = null);

/// <summary>
/// Der Stand einer Phase, wie ihn ein Format zur Arbeit braucht.
///
/// Bewusst ein schlichtes Datenpaket und nicht die Phase selbst: ein Format soll
/// rechnen, nicht verändern. Wer das trennt, kann jede Paarungslogik ohne
/// Aggregat und ohne Datenbank testen.
/// </summary>
public sealed record PhaseState(
    Guid PhaseId,
    PhaseDefinition Definition,
    MatchFormat MatchFormat,
    IReadOnlyList<SeededEntry> Entries,
    IReadOnlyList<Match> Matches);

/// <summary>
/// Ein Startplatz einer Phase mit seiner Setzposition.
///
/// In der ersten Phase ist das eine Meldung. In jeder weiteren ist es zunächst
/// nur ein Platz — „Erster der Gruppe A" —, der noch niemandem gehört: das
/// Bracket der Endrunde steht, bevor die Gruppen ausgespielt sind (ADR-0001).
/// Deshalb trägt der Startplatz seine Herkunft mit und nicht bloß eine Id.
/// </summary>
public sealed record SeededEntry(Guid EntryId, int? Seed, string DisplayName)
{
    /// <summary>
    /// Woher der Platz kommt. Bei einer Meldung ist das die Meldung selbst.
    /// </summary>
    public ParticipantRef Origin { get; init; } =
        EntryId == Guid.Empty ? ParticipantRef.Open : ParticipantRef.Of(EntryId);

    /// <summary>Steht schon fest, wer diesen Platz einnimmt?</summary>
    public bool IsSettled => EntryId != Guid.Empty;

    /// <summary>
    /// Ein noch unbesetzter Platz, etwa der Gruppensieger einer laufenden
    /// Vorphase.
    /// </summary>
    public static SeededEntry ForSlot(ParticipantRef origin, int seed, string label)
    {
        ArgumentNullException.ThrowIfNull(origin);

        return new SeededEntry(Guid.Empty, seed, label) { Origin = origin };
    }
}

/// <summary>Ein Platz in der Tabelle einer Phase.</summary>
public sealed record Standing(
    int Rank,
    Guid EntryId,
    string DisplayName,
    string? Group,
    int Played,
    int Won,
    int Lost,
    int Points,
    int SetsWon,
    int SetsLost,
    int GamesWon,
    int GamesLost)
{
    public int SetDifference => SetsWon - SetsLost;

    public int GameDifference => GamesWon - GamesLost;
}

/// <summary>Die Tabelle einer Phase, gegebenenfalls nach Gruppen getrennt.</summary>
public sealed record Standings(IReadOnlyList<Standing> Places)
{
    public static Standings Empty { get; } = new([]);

    public IEnumerable<Standing> InGroup(string? group) =>
        Places.Where(p => p.Group == group).OrderBy(p => p.Rank);

    public IReadOnlyList<string?> Groups =>
        Places.Select(p => p.Group).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
}

/// <summary>
/// Die tragende Abstraktion aus ADR-0001.
///
/// Jedes Format beantwortet dieselben drei Fragen, und die Unterschiede stecken
/// darin, <em>wann</em> es Paarungen liefert: Round Robin gibt alle auf einmal
/// aus, das Schweizer System genau eine Runde, K.o. füllt die nächste Runde,
/// sobald die Vorgänger entschieden sind.
///
/// Alles Weitere — Satzformat, Tiebreak-Regeln, Punktesystem, Gruppenanzahl,
/// Qualifikantenzahl — ist Parameter, nicht Vererbung. Sobald jemand
/// <c>RoundRobinMitSonderregel : RoundRobinFormat</c> schreibt, wurde ein
/// Parameter übersehen.
/// </summary>
public interface IPhaseFormat
{
    PhaseFormatKind Kind { get; }

    /// <summary>
    /// Die nächste Menge an Paarungen. Liefert eine leere Menge, wenn im
    /// Augenblick nichts anzusetzen ist — etwa weil eine Runde noch läuft.
    /// </summary>
    IReadOnlyList<Pairing> GeneratePairings(PhaseState state);

    bool IsComplete(PhaseState state);

    Standings ComputeStandings(PhaseState state);
}
