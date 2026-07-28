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
    /// Legt die Phasen des Turniers an und erzeugt die Paarungen, die sich schon
    /// bestimmen lassen.
    ///
    /// Nur die erste Phase bekommt Paarungen: alle weiteren beziehen ihre
    /// Teilnehmer aus einer Vorphase und werden erst gefüllt, wenn diese
    /// abgeschlossen ist (ADR-0001).
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

        foreach (var phaseDefinition in definition.Phases.OrderBy(p => p.Ordinal))
        {
            var phase = new Phase(
                Guid.NewGuid(),
                tournament.Id,
                phaseDefinition.Ordinal,
                phaseDefinition.Format,
                phaseDefinition.Name);

            if (phaseDefinition.Ordinal == 1)
            {
                var format = PhaseFormats.For(phaseDefinition.Format);
                var state = new PhaseState(
                    phase.Id,
                    phaseDefinition,
                    definition.MatchFormatOf(phaseDefinition),
                    seeded,
                    phase.Matches);

                phase.AddPairings(format.GeneratePairings(state));
            }

            _phases.Add(phase);
            created.Add(phase);
        }

        return created;
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
