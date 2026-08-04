using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.PublicView;

/// <summary>
/// Baut die öffentliche Sicht aus den Aggregaten.
///
/// Der Bauplan ist zugleich die Datenschutzgrenze: alles, was hier nicht
/// ausdrücklich übernommen wird, bleibt intern. Deshalb ist das eine reine
/// Abbildung ohne Zugriff auf Ablagen — sie lässt sich vollständig prüfen, ohne
/// eine Datenbank zu starten, und es gibt keinen zweiten Weg, auf dem Daten in
/// die Projektion gelangen.
/// </summary>
public static class TournamentViewBuilder
{
    public static PublicTournamentView Build(
        Tournament tournament,
        Club club,
        IReadOnlyList<Phase> phases,
        IReadOnlyDictionary<Guid, Standings> standingsByPhase,
        IReadOnlyDictionary<Guid, string> participantNameByEntry,
        IReadOnlyList<CourtAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(tournament);
        ArgumentNullException.ThrowIfNull(club);
        ArgumentNullException.ThrowIfNull(phases);

        var seedByEntry = tournament.Entries
            .Where(entry => entry.Seed is not null)
            .ToDictionary(entry => entry.Id, entry => entry.Seed);

        var courtNames = club.Courts.ToDictionary(court => court.Id, court => court.Name);
        // Je Match die Zuweisung, die gerade gilt. Nach einer Unterbrechung gibt
        // es zwei: die unterbrochene als Historie und die Fortsetzung. Gezeigt
        // wird, was läuft — sonst schickt der Aushang die Zuschauer auf den Platz
        // von vorhin.
        var activeByMatch = assignments
            .Where(assignment => assignment.Status != AssignmentStatus.Finished)
            .GroupBy(assignment => assignment.MatchId)
            .ToDictionary(group => group.Key, group => group.OrderBy(CourtQueue.Liveness).ThenByDescending(a => a.Version).First());

        var context = new BuildContext(participantNameByEntry, seedByEntry, courtNames, activeByMatch);

        return new PublicTournamentView(
            tournament.Id,
            tournament.Name,
            club.Name,
            tournament.StartsOn,
            tournament.EndsOn,
            tournament.State,
            tournament.SchedulingMode,
            [.. phases.OrderBy(phase => phase.Ordinal).Select(phase => Describe(phase, standingsByPhase, context))],
            Describe(club, assignments));
    }

    private sealed record BuildContext(
        IReadOnlyDictionary<Guid, string> NameByEntry,
        IReadOnlyDictionary<Guid, int?> SeedByEntry,
        IReadOnlyDictionary<Guid, string> CourtNames,
        IReadOnlyDictionary<Guid, CourtAssignment> AssignmentByMatch);

    private static PublicPhaseView Describe(
        Phase phase,
        IReadOnlyDictionary<Guid, Standings> standingsByPhase,
        BuildContext context)
    {
        var matches = phase.Matches.OrderBy(m => m.Round).ThenBy(m => m.Position).ToList();
        var labels = MatchLabels(matches);

        var standings = standingsByPhase.TryGetValue(phase.Id, out var table)
            ? table.Places.Select(Describe).ToList()
            : [];

        return new PublicPhaseView(
            phase.Id,
            phase.Ordinal,
            phase.Name,
            phase.Status,
            [.. matches.Select(match => Describe(match, labels, context))],
            standings);
    }

    /// <summary>
    /// Ein sprechender Name je Match, für die Herkunftsangabe der Folgerunden.
    ///
    /// Die Regel steht in <see cref="MatchOrigins"/>, weil die Ansicht der
    /// Turnierleitung dieselbe braucht — eine zweite Fassung hier hatte zur
    /// Folge, dass die öffentliche Ansicht lesbar war und die interne Kennungen
    /// zeigte.
    /// </summary>
    private static Dictionary<Guid, string> MatchLabels(IReadOnlyList<Match> matches) =>
        MatchOrigins.LabelsOf(matches);

    private static PublicMatchView Describe(
        Match match,
        IReadOnlyDictionary<Guid, string> labels,
        BuildContext context)
    {
        var assignment = context.AssignmentByMatch.GetValueOrDefault(match.Id);

        return new PublicMatchView(
            match.Id,
            match.Round,
            match.Position,
            match.Label,
            match.Group,
            Describe(match.Side1, labels, context),
            Describe(match.Side2, labels, context),
            match.Status,
            match.Score?.Outcome,
            match.Score?.WinnerSide,
            match.Score?.ToString(),
            assignment is null ? null : context.CourtNames.GetValueOrDefault(assignment.CourtId),
            assignment?.EarliestStart,
            assignment?.PlannedStart,
            assignment?.Status);
    }

    private static PublicSideView Describe(
        MatchSide side,
        IReadOnlyDictionary<Guid, string> labels,
        BuildContext context) => new(
            side.EntryId is { } id ? context.NameByEntry.GetValueOrDefault(id) : null,
            side.EntryId is { } seeded ? context.SeedByEntry.GetValueOrDefault(seeded) : null,
            Describe(side.Origin, labels));

    private static string Describe(ParticipantRef origin, IReadOnlyDictionary<Guid, string> labels) =>
        MatchOrigins.Describe(origin, labels);

    private static PublicStandingView Describe(Standing standing) => new(
        standing.Rank,
        standing.DisplayName,
        standing.Group,
        standing.Played,
        standing.Won,
        standing.Lost,
        standing.Points,
        standing.SetsWon,
        standing.SetsLost,
        standing.GamesWon,
        standing.GamesLost);

    /// <summary>
    /// Die Plätze mit ihrer Warteschlange. Öffnungszeiten und Sperren bleiben
    /// draußen: eine Sperre trägt eine interne Notiz, und ihr Grund geht
    /// niemanden etwas an, der wissen will, wo als Nächstes gespielt wird.
    /// </summary>
    private static IReadOnlyList<PublicCourtView> Describe(
        Club club,
        IReadOnlyList<CourtAssignment> assignments)
    {
        // Was auf dem Platz steht: erst das laufende oder aufgerufene Match, dann
        // die Wartenden in ihrer Reihenfolge. Eine unterbrochene Zuweisung wartet
        // nicht auf diesen Platz, sondern auf ihre Fortsetzung — die kann anderswo
        // stattfinden (ADR-0002).
        var byCourt = assignments
            .Where(assignment => assignment.Status
                is AssignmentStatus.Called or AssignmentStatus.Running or AssignmentStatus.Planned)
            .GroupBy(assignment => assignment.CourtId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(CourtQueue.Liveness).ThenBy(a => a.SequenceOnCourt).ToList());

        // Auch ein stillgelegter Platz wird gezeigt, solange noch ein Match auf
        // ihm steht: sonst trüge das Match einen Platznamen, den die Platzliste
        // nicht kennt — und die Warteschlange, die am Turniertag die eigentliche
        // Aussage ist, fehlte ganz (ADR-0002).
        return
        [
            .. club.Courts
                .Where(court => court.IsActive || byCourt.ContainsKey(court.Id))
                .OrderBy(court => court.Name, StringComparer.CurrentCulture)
                .Select(court => new PublicCourtView(
                    court.Id,
                    court.Name,
                    [
                        .. byCourt.GetValueOrDefault(court.Id, [])
                            .Select(a => new PublicCourtSlotView(
                                a.MatchId, a.SequenceOnCourt, a.Status, a.EarliestStart, a.PlannedStart)),
                    ])),
        ];
    }
}
