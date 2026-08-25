import { useState } from 'react'
import { Icon } from '../core/Icon'
import { Sheet } from './Sheet'
import { useWorkspace } from '../../state/WorkspaceContext'
import { tournamentStateLabel } from '../../lib/labels'
import { formatDateRange } from '../../lib/time'

/**
 * Die Kopfleiste: welches Turnier gerade gemeint ist.
 *
 * Sie steht einmal in der Hülle und nicht siebenmal in den Bildschirmen. Vorher
 * trug jeder Bildschirm seine eigene Kopfzeile mit Titel, Kennung, Untertitel
 * und Auswahl — dieselbe Zeile, siebenmal leicht anders, und am Telefon vier
 * Zeilen hoch, bevor der Inhalt begann.
 *
 * Was hier steht, beantwortet die einzige Frage, die an jeder Stelle offen
 * ist: an welchem Turnier arbeite ich, und wie weit ist es. Wo man sich darin
 * befindet, sagt die Navigation.
 */
export function AppBar() {
  const { tournament, tournaments, selectTournament } = useWorkspace()
  const [open, setOpen] = useState(false)

  return (
    <>
      <header className="md-appbar">
        <button
          type="button"
          className="md-appbar__pick"
          aria-label="Turnier wählen"
          aria-expanded={open}
          onClick={() => setOpen(true)}
        >
          <span className="md-appbar__text">
            <span className="md-appbar__name">{tournament?.name ?? 'Kein Turnier'}</span>
            <span className="md-appbar__meta">
              {tournament
                ? `${tournament.venue.name} · ${formatDateRange(tournament.startsOn, tournament.endsOn)} · ${tournamentStateLabel[tournament.state]}`
                : 'Turnier wählen oder anlegen'}
            </span>
          </span>
          <Icon name="chevron" size={18} />
        </button>
      </header>

      <Sheet open={open} title="Turnier wählen" onClose={() => setOpen(false)}>
        {tournaments.length === 0 ? (
          <div className="md-hint">
            Noch kein Turnier. Über „Mehr → Turnier anlegen" entsteht das erste.
          </div>
        ) : (
          <div className="md-sheet__list">
            {tournaments.map((entry) => (
              <button
                key={entry.id}
                type="button"
                className="md-sheet__item"
                aria-current={entry.id === tournament?.id ? 'true' : undefined}
                onClick={() => {
                  setOpen(false)
                  selectTournament(entry.id)
                }}
              >
                {entry.id === tournament?.id ? (
                  <Icon name="check" />
                ) : (
                  <span className="md-sheet__spacer" aria-hidden="true" />
                )}
                <span className="md-sheet__stack">
                  <span>{entry.name}</span>
                  <span className="md-sheet__sub">
                    {entry.venueName} · {tournamentStateLabel[entry.state]}
                  </span>
                </span>
              </button>
            ))}
          </div>
        )}
      </Sheet>
    </>
  )
}
