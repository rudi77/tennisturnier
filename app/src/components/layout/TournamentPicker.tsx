import { useWorkspace } from '../../state/WorkspaceContext'
import { tournamentStateLabel } from '../../lib/labels'

/**
 * Verein und Turnier auswählen.
 *
 * Der Prototyp kannte genau ein Turnier; die API ist club-scoped. Die Auswahl
 * steht deshalb in der Kopfzeile statt in einer eigenen Ebene — die
 * Turnierleitung arbeitet an einem Tag mit genau einem Turnier und soll nicht
 * dauernd navigieren, aber sehen, welches gemeint ist.
 */
export function TournamentPicker() {
  const { clubs, club, tournaments, tournament, selectClub, selectTournament } = useWorkspace()

  return (
    <div style={{ display: 'flex', gap: 'var(--sp-4)', alignItems: 'center', flexWrap: 'wrap' }}>
      {clubs.length > 1 && (
        <select
          className="md-input"
          aria-label="Verein"
          value={club?.id ?? ''}
          onChange={(event) => selectClub(event.target.value)}
        >
          {clubs.map((entry) => (
            <option key={entry.id} value={entry.id}>
              {entry.name}
            </option>
          ))}
        </select>
      )}

      <select
        className="md-input"
        aria-label="Turnier"
        value={tournament?.id ?? ''}
        onChange={(event) => selectTournament(event.target.value)}
        disabled={tournaments.length === 0}
        style={{ maxWidth: 300 }}
      >
        {tournaments.length === 0 && <option value="">kein Turnier</option>}
        {tournaments.map((entry) => (
          <option key={entry.id} value={entry.id}>
            {entry.name} · {tournamentStateLabel[entry.state]}
          </option>
        ))}
      </select>
    </div>
  )
}
