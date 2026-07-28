using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// Schweizer System: alle spielen jede Runde, gepaart wird nach Punktestand.
///
/// Der Unterschied zu den anderen Formaten steckt nicht in der Tabelle, sondern
/// im Zeitpunkt: K.o. und Gruppenphase legen ihre Paarungen beim Auslosen an,
/// hier entsteht jede Runde erst, wenn die vorige durch ist. Ein Draw, der schon
/// die zweite Runde zeigt, gäbe es nicht — sie hängt davon ab, wie die erste
/// ausgeht (ADR-0001).
/// </summary>
public sealed class SwissFormat : IPhaseFormat
{
    public PhaseFormatKind Kind => PhaseFormatKind.Swiss;

    public IReadOnlyList<Pairing> GeneratePairings(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var players = state.Entries.Where(entry => entry.IsSettled).ToList();
        var rounds = RoundsOf(state.Definition, players.Count);
        var playedRounds = PlayedRounds(state);

        if (playedRounds == 0)
        {
            RequirePlayableField(players.Count, rounds);
        }

        // Eine laufende Runde wird nicht überholt: die Paarung der nächsten hängt
        // an ihrem Ausgang.
        if (playedRounds >= rounds || state.Matches.Any(match => match.Status != MatchStatus.Finished))
        {
            return [];
        }

        return NextRound(state, players, playedRounds + 1);
    }

    /// <summary>
    /// Durch, wenn die vorgesehene Rundenzahl gespielt ist.
    ///
    /// Nicht schon dann, wenn alle angelegten Matches entschieden sind: nach der
    /// dritten von sieben Runden trifft das ebenso zu, und eine Folgephase
    /// bekäme ihre Teilnehmer aus einer Tabelle, die noch vier Runden vor sich
    /// hat.
    /// </summary>
    public bool IsComplete(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var players = state.Entries.Where(entry => entry.IsSettled).Count();

        return state.Matches.Count > 0
            && PlayedRounds(state) >= RoundsOf(state.Definition, players)
            && state.Matches.All(match => match.Status == MatchStatus.Finished);
    }

    public Standings ComputeStandings(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var table = state.Entries
            .Where(entry => entry.IsSettled)
            .Select(entry => StandingsBuilder.Accumulate(entry, null, state))
            .ToList();

        var context = StandingsBuilder.ContextOf(table, state.Matches);

        return new Standings([.. StandingsBuilder.Rank(table, state.Definition.Tiebreakers, context)]);
    }

    /// <summary>
    /// Alle Runden nach der ersten, in der noch etwas offen ist.
    ///
    /// Wird ein Ergebnis korrigiert, ändert sich die Tabelle — und damit die
    /// Paarung jeder Runde, die daraus entstanden ist. Sie stehen zu lassen
    /// hieße, nach einer Korrektur mit Paarungen weiterzuspielen, die aus einer
    /// Tabelle stammen, die es nicht mehr gibt. Sie werden deshalb zurückgenommen
    /// und beim nächsten Ergebnis neu gepaart.
    /// </summary>
    public IReadOnlyList<Guid> ObsoletePairings(PhaseState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var open = state.Matches
            .Where(match => match.Status != MatchStatus.Finished)
            .Select(match => match.Round)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

        return [.. state.Matches.Where(match => match.Round > open).Select(match => match.Id)];
    }

    /// <summary>
    /// Die Rundenzahl aus der Definition, sonst <c>ceil(log2(n))</c> — so viele
    /// Runden, wie ein K.-o.-Baum desselben Feldes hätte. Das ist die Zahl, ab
    /// der ein Sieger ohne Niederlage überhaupt möglich ist.
    /// </summary>
    internal static int RoundsOf(PhaseDefinition definition, int players) =>
        definition.Rounds ?? Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(players, 2))));

    /// <summary>
    /// Die höchste Runde, die vollständig entschieden ist.
    /// </summary>
    private static int PlayedRounds(PhaseState state)
    {
        var finished = 0;

        foreach (var round in state.Matches.Select(match => match.Round).Distinct().OrderBy(round => round))
        {
            if (state.Matches.Any(match => match.Round == round && match.Status != MatchStatus.Finished))
            {
                break;
            }

            finished = round;
        }

        return finished;
    }

    /// <summary>
    /// Weist ein Turnier zurück, das mehr Runden vorsieht, als das Feld hergibt.
    ///
    /// Bei gerader Teilnehmerzahl hat jeder n-1 mögliche Gegner, bei ungerader
    /// kommt ein Freilos hinzu. Darüber hinaus gibt es keine wiederholungsfreie
    /// Paarung mehr — und das soll beim Auslosen auffallen und nicht mitten im
    /// Turnier, wenn die Runde ansteht, die nicht mehr geht.
    /// </summary>
    private static void RequirePlayableField(int players, int rounds)
    {
        if (players < 2)
        {
            throw new DomainException(
                $"Ein Schweizer System braucht mindestens zwei Teilnehmer, hatte {players}.");
        }

        var possible = players % 2 == 0 ? players - 1 : players;

        if (rounds > possible)
        {
            throw new DomainException(
                $"{rounds} Runden lassen sich mit {players} Teilnehmern nicht wiederholungsfrei spielen; " +
                $"möglich sind {possible}.");
        }
    }

    private static IReadOnlyList<Pairing> NextRound(
        PhaseState state,
        IReadOnlyList<SeededEntry> players,
        int round)
    {
        var standings = ComputeOrder(state, players);
        var order = standings.Select(place => place.EntryId).ToList();
        var points = standings.ToDictionary(place => place.EntryId, place => place.Points);
        var previous = OpponentsSoFar(state.Matches);

        var pairings = new List<Pairing>();
        var position = 0;

        if (SwissPairing.PickBye(order, ByeReceivers(state.Matches)) is { } resting)
        {
            order.Remove(resting);

            pairings.Add(new Pairing(
                round,
                ++position,
                ParticipantRef.Of(resting),
                ParticipantRef.ByeSlot,
                Label(round)));
        }

        foreach (var (side1, side2) in SwissPairing.PairRound(order, points, previous))
        {
            pairings.Add(new Pairing(
                round, ++position, ParticipantRef.Of(side1), ParticipantRef.Of(side2), Label(round)));
        }

        return pairings;
    }

    private static string Label(int round) => $"Runde {round}";

    /// <summary>
    /// Die Tabelle als Reihenfolge. Vor der ersten Runde hat niemand Punkte, und
    /// die Tiebreaker-Kette fällt bis auf die Setzung durch — die erste Runde
    /// paart damit den Ersten der Setzliste gegen den Ersten der unteren Hälfte,
    /// genau wie es das Dutch-System vorsieht.
    /// </summary>
    private static IReadOnlyList<Standing> ComputeOrder(PhaseState state, IReadOnlyList<SeededEntry> players)
    {
        var table = players.Select(entry => StandingsBuilder.Accumulate(entry, null, state)).ToList();
        var context = StandingsBuilder.ContextOf(table, state.Matches);

        return [.. StandingsBuilder.Rank(table, state.Definition.Tiebreakers, context)];
    }

    private static IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> OpponentsSoFar(IReadOnlyList<Match> matches)
    {
        var opponents = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var match in matches)
        {
            if (match.Side1.EntryId is not { } side1 || match.Side2.EntryId is not { } side2)
            {
                continue;
            }

            Met(opponents, side1).Add(side2);
            Met(opponents, side2).Add(side1);
        }

        return opponents.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<Guid>)pair.Value);
    }

    /// <summary>
    /// Wer schon einmal ausgesetzt hat. Ein Freilos-Match hat nur eine besetzte
    /// Seite — und die gehört dem, der es bekommen hat.
    /// </summary>
    private static IReadOnlySet<Guid> ByeReceivers(IReadOnlyList<Match> matches)
    {
        var received = new HashSet<Guid>();

        foreach (var match in matches.Where(match => match.HasBye))
        {
            if ((match.Side1.IsBye ? match.Side2.EntryId : match.Side1.EntryId) is { } entryId)
            {
                received.Add(entryId);
            }
        }

        return received;
    }

    private static HashSet<Guid> Met(Dictionary<Guid, HashSet<Guid>> map, Guid entryId)
    {
        if (!map.TryGetValue(entryId, out var set))
        {
            set = [];
            map[entryId] = set;
        }

        return set;
    }
}
