import { useState } from 'react'
import { tournaments as tournamentApi } from '../../api/endpoints'
import { TournamentState, type TournamentDetail } from '../../api/types'
import { useToast } from '../../hooks/useToast'

/**
 * Turnier starten und abbrechen.
 *
 * Beide Endpunkte gab es von Anfang an; aufgerufen hat sie nie jemand. Der
 * Start folgte bis hierher allein aus dem ersten Ergebnis — richtig gedacht,
 * aber unsichtbar: wer ein ausgelostes Turnier vor sich hatte, fand keinen Weg
 * weiter und musste raten, dass die Ergebniseingabe der Startknopf ist.
 *
 * Der automatische Start bleibt trotzdem bestehen. Er ist die Rückfallebene für
 * den, der gleich das erste Ergebnis einträgt, und kostet nichts — ein Turnier,
 * das bereits läuft, hat für diesen Knopf keine Verwendung mehr und zeigt ihn
 * nicht.
 *
 * Der Abbruch ist endgültig und fragt deshalb nach. Er ist kein Löschen: das
 * Turnier bleibt mit allem, was gespielt wurde, lesbar — es wird nur nicht mehr
 * fortgesetzt.
 */
type Action = 'start' | 'abandon'

/** Endpunkt, Erfolgsmeldung und Fehlerüberschrift je Zug — an einer Stelle. */
const ACTIONS: Record<
  Action,
  { call: (id: string) => Promise<void>; done: string; label: string }
> = {
  start: {
    call: (id) => tournamentApi.start(id),
    done: 'Turnier gestartet — ab jetzt werden Ergebnisse erfasst',
    label: 'Start',
  },
  abandon: {
    call: (id) => tournamentApi.abandon(id),
    done: 'Turnier abgebrochen. Was gespielt wurde, bleibt lesbar.',
    label: 'Abbruch',
  },
}

export function TournamentActions({
  tournament,
  onChanged,
}: {
  tournament: TournamentDetail
  onChanged: () => Promise<void>
}) {
  const { show, showError } = useToast()
  const [busy, setBusy] = useState<Action | null>(null)
  const [confirming, setConfirming] = useState(false)

  const canStart = tournament.state === TournamentState.DrawGenerated
  const canAbandon =
    tournament.state !== TournamentState.Completed &&
    tournament.state !== TournamentState.Abandoned

  const run = async (action: Action) => {
    const { call, done, label } = ACTIONS[action]
    setBusy(action)
    try {
      await call(tournament.id)
      show(done)
      setConfirming(false)
      await onChanged()
    } catch (cause) {
      showError(cause, label)
    } finally {
      setBusy(null)
    }
  }

  if (!canStart && !canAbandon) return null

  return (
    <div style={{ display: 'flex', gap: 'var(--sp-4)', flexWrap: 'wrap', alignItems: 'center' }}>
      {canStart && (
        <button
          type="button"
          className="md-btn md-btn--accent"
          disabled={busy !== null}
          onClick={() => void run('start')}
        >
          {busy === 'start' ? 'Startet …' : 'Turnier starten'}
        </button>
      )}

      {canAbandon &&
        (confirming ? (
          <>
            <span style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-2)' }}>Wirklich abbrechen?</span>
            <button
              type="button"
              className="md-btn md-btn--danger"
              disabled={busy !== null}
              onClick={() => void run('abandon')}
            >
              {busy === 'abandon' ? 'Bricht ab …' : 'Ja, abbrechen'}
            </button>
            <button type="button" className="md-btn" onClick={() => setConfirming(false)}>
              Zurück
            </button>
          </>
        ) : (
          <button type="button" className="md-btn" onClick={() => setConfirming(true)}>
            Turnier abbrechen
          </button>
        ))}
    </div>
  )
}
