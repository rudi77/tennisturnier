import { useWorkspace } from '../../state/WorkspaceContext'
import { tournamentStateLabel } from '../../lib/labels'

/**
 * Das Turnier auswählen.
 *
 * Hier stand einmal ein Verein daneben — er ist als Wurzel entfallen, und damit
 * bleibt eine Auswahl. Sie steht in der Kopfzeile statt in einer eigenen Ebene:
 * die Turnierleitung arbeitet an einem Tag mit genau einem Turnier und soll
 * nicht dauernd navigieren, aber sehen, welches gemeint ist.
 */
export function TournamentPicker() {
  const { tournaments, tournament, selectTournament } = useWorkspace()

  return (
    <div style={{ display: 'flex', gap: 'var(--sp-4)', alignItems: 'center', flexWrap: 'wrap' }}>
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
