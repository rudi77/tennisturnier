using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Phases;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Baut aus dem eingefrorenen Format die Phasen und ihre Paarungen.
///
/// Sitzt bewusst in der Anwendungsschicht und nicht im Turnier-Aggregat: das
/// Turnier weiß, <em>dass</em> ausgelost wurde, die Phasen sind ein eigenes
/// Aggregat mit eigener Lebensdauer. Die Formate selbst rechnen in der Domäne.
/// </summary>
public sealed class DrawBuilder
{
    private readonly IPhaseRepository _phases;
    private readonly IPlayerRepository _players;

    public DrawBuilder(IPhaseRepository phases, IPlayerRepository players)
    {
        _phases = phases;
        _players = players;
    }

    /// <summary>
    /// Legt die Phasen des Turniers an und erzeugt ihre Paarungen.
    ///
    /// Auch die der Folgephasen: deren Startplätze sind zunächst Gruppenplätze —
    /// „Erster der Gruppe A" —, und genau daraus entsteht das Bracket der
    /// Endrunde, während die Gruppen noch laufen. Aufgelöst werden sie später
    /// mit demselben Mechanismus wie „Sieger aus Halbfinale 1" (ADR-0001).
    /// </summary>
    public async Task<IReadOnlyList<Phase>> BuildAsync(
        Tournament tournament,
        CancellationToken cancellationToken = default)
    {
        var snapshot = tournament.Format
            ?? throw new DomainException("Ohne eingefrorenes Format lässt sich kein Draw bauen.");

        var definition = snapshot.Definition;

        foreach (var phaseDefinition in definition.Phases)
        {
            if (!PhaseFormats.IsSupported(phaseDefinition.Format))
            {
                throw new DomainException(
                    $"Das Format {phaseDefinition.Format} ist noch nicht umgesetzt. " +
                    "Ein Turnier lässt sich damit nicht auslosen.");
            }
        }

        var seeded = await SeedEntriesAsync(tournament, cancellationToken);
        var created = new List<Phase>(definition.Phases.Count);
        var entriesByOrdinal = new Dictionary<int, IReadOnlyList<SeededEntry>>();

        foreach (var phaseDefinition in definition.Phases.OrderBy(p => p.Ordinal))
        {
            var phase = new Phase(
                Guid.NewGuid(),
                tournament.Id,
                phaseDefinition.Ordinal,
                phaseDefinition.Format,
                phaseDefinition.Name);

            var entries = StartersOf(phaseDefinition, seeded, created, definition, entriesByOrdinal);
            entriesByOrdinal[phaseDefinition.Ordinal] = entries;

            var state = new PhaseState(
                phase.Id,
                phaseDefinition,
                definition.MatchFormatOf(phaseDefinition),
                entries,
                phase.Matches);

            phase.AddPairings(PhaseFormats.For(phaseDefinition.Format).GeneratePairings(state));

            _phases.Add(phase);
            created.Add(phase);
        }

        return created;
    }

    /// <summary>
    /// Die Startplätze einer Phase: in der ersten die Meldungen, in jeder
    /// weiteren die Plätze, die aus der genannten Vorphase hervorgehen.
    /// </summary>
    private static IReadOnlyList<SeededEntry> StartersOf(
        PhaseDefinition phaseDefinition,
        IReadOnlyList<SeededEntry> seeded,
        IReadOnlyList<Phase> created,
        FormatDefinition definition,
        IReadOnlyDictionary<int, IReadOnlyList<SeededEntry>> entriesByOrdinal)
    {
        if (phaseDefinition.Qualification is not { } qualification)
        {
            return seeded;
        }

        var source = created.FirstOrDefault(p => p.Ordinal == qualification.FromPhase)
            ?? throw new DomainException(
                $"Phase {phaseDefinition.Ordinal} bezieht ihre Teilnehmer aus Phase " +
                $"{qualification.FromPhase}, die es nicht gibt.");

        var sourceDefinition = definition.Phases.First(p => p.Ordinal == qualification.FromPhase);

        return Qualifier.Slots(
            qualification,
            source.Id,
            sourceDefinition,
            entriesByOrdinal[qualification.FromPhase]);
    }

    /// <summary>
    /// Entfernt alle Phasen eines Turniers. Gehört zum Rückschritt, der die
    /// Auslosung zurücknimmt — ein Draw, der eine Nachmeldung überlebt, wäre
    /// genau die stille Änderung, die ADR-0001 ausschließt.
    /// </summary>
    public async Task DiscardAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var phases = await _phases.ListByTournamentAsync(tournamentId, cancellationToken);

        _phases.RemoveRange(phases);
    }

    /// <summary>
    /// Die angenommenen Meldungen mit Setzung und Anzeigename.
    ///
    /// Der Name kommt aus dem Teilnehmer und wird hier einmal aufgelöst: die
    /// Formate brauchen ihn für eine nachvollziehbare Reihenfolge der
    /// Ungesetzten, sollen aber nicht selbst auf Repositories zugreifen.
    /// </summary>
    private async Task<IReadOnlyList<SeededEntry>> SeedEntriesAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var accepted = tournament.AcceptedEntries;
        var participantIds = accepted.Select(e => e.ParticipantId).Distinct().ToList();
        var participants = await _players.FindParticipantsAsync(participantIds, cancellationToken);
        var names = participants.ToDictionary(p => p.Id, p => p.DisplayName);

        return accepted
            .Select(e => new SeededEntry(e.Id, e.Seed, names.GetValueOrDefault(e.ParticipantId, e.Id.ToString())))
            .ToList();
    }
}
