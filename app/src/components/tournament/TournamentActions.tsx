import { useState } from 'react'
import { tournaments as tournamentApi } from '../../api/endpoints'
import { TournamentState, type TournamentDetail } from '../../api/types'
import { useToast } from '../../hooks/useToast'

/**
 * Turnier starten, abbrechen, löschen.
 *
 * Start und Abbruch gab es als Endpunkte von Anfang an; aufgerufen hat sie nie
 * jemand. Der Start folgte allein aus dem ersten Ergebnis — richtig gedacht,
 * aber unsichtbar: wer ein ausgelostes Turnier vor sich hatte, fand keinen Weg
 * weiter. Der automatische Start bleibt als Rückfallebene bestehen.
 *
 * Abbrechen und Löschen sind ausdrücklich zweierlei, und die Beschriftung sagt
 * das auch:
 *
 *  - <b>Abbrechen</b> beendet ein Turnier und lässt lesbar, was gespielt wurde.
 *    Für das abgesagte Turnier, den Regen, die zu wenigen Meldungen.
 *  - <b>Löschen</b> lässt nichts. Für das, was gar nicht hätte entstehen sollen
 *    — den Probelauf, den Tippfehler, das doppelt angelegte Turnier.
 *
 * Beide fragen nach, und die Rückfrage nennt die Folge statt „sind Sie sicher".
 */
type Action = 'start' | 'abandon' | 'delete'

/** Endpunkt, Erfolgsmeldung, Fehlerüberschrift und Rückfrage je Zug. */
const ACTIONS: Record<
  Action,
  {
    call: (id: string) => Promise<void>
    done: string
    label: string
    /** Der Satz der Rückfrage. Leer heißt: keine — der Start hat keine Folge. */
    confirm?: string
    button: string
  }
> = {
  start: {
    call: (id) => tournamentApi.start(id),
    done: 'Turnier gestartet — ab jetzt werden Ergebnisse erfasst',
    label: 'Start',
    button: 'Turnier starten',
  },
  abandon: {
    call: (id) => tournamentApi.abandon(id),
    done: 'Turnier abgebrochen. Was gespielt wurde, bleibt lesbar.',
    label: 'Abbruch',
    confirm: 'Abbrechen beendet das Turnier. Gespieltes bleibt lesbar.',
    button: 'Turnier abbrechen',
  },
  delete: {
    call: (id) => tournamentApi.remove(id),
    done: 'Turnier gelöscht',
    label: 'Löschen',
    confirm:
      'Löschen entfernt Meldungen, Draw, Ergebnisse und den Anmeldelink. Das lässt sich nicht rückgängig machen.',
    button: 'Turnier löschen',
  },
}

export function TournamentActions({
  tournament,
  onChanged,
  onDeleted,
}: {
  tournament: TournamentDetail
  onChanged: () => Promise<void>
  /**
   * Wohin, wenn das Turnier weg ist. Ohne diesen Weg liefe das Nachladen gegen
   * eine Kennung, die es nicht mehr gibt — und der Benutzer sähe einen Fehler
   * für etwas, das gerade geklappt hat.
   */
  onDeleted?: () => void
}) {
  const { show, showError } = useToast()
  const [busy, setBusy] = useState<Action | null>(null)
  const [confirming, setConfirming] = useState<Action | null>(null)

  const canStart = tournament.state === TournamentState.DrawGenerated
  const canAbandon =
    tournament.state !== TournamentState.Completed &&
    tournament.state !== TournamentState.Abandoned

  const run = async (action: Action) => {
    const { call, done, label } = ACTIONS[action]
    setBusy(action)
    try {
      await call(tournament.id)
      setConfirming(null)

      if (action === 'delete') {
        // Zuerst weg von hier, dann nachladen: sonst holt die Ansicht ein
        // Turnier, das es nicht mehr gibt.
        onDeleted?.()
      }

      // Erst nachladen, dann melden — wer die Meldung liest, schaut auf den
      // Bildschirm. Dieselbe Reihenfolge wie in useAction.
      await onChanged()
      show(done)
    } catch (cause) {
      showError(cause, label)
    } finally {
      setBusy(null)
    }
  }

  if (confirming) {
    const { confirm, button } = ACTIONS[confirming]

    return (
      <div className="md-flow__row" style={{ alignItems: 'center' }}>
        <span className="md-hint">{confirm}</span>
        <button
          type="button"
          className="md-btn md-btn--danger"
          disabled={busy !== null}
          onClick={() => void run(confirming)}
        >
          {busy ? 'Läuft …' : `Ja, ${button.toLowerCase()}`}
        </button>
        <button type="button" className="md-btn" onClick={() => setConfirming(null)}>
          Zurück
        </button>
      </div>
    )
  }

  return (
    <div className="md-flow__row" style={{ alignItems: 'center' }}>
      {canStart && (
        <button
          type="button"
          className="md-btn md-btn--accent"
          disabled={busy !== null}
          onClick={() => void run('start')}
        >
          {busy === 'start' ? 'Startet …' : ACTIONS.start.button}
        </button>
      )}

      {canAbandon && (
        <button type="button" className="md-btn" onClick={() => setConfirming('abandon')}>
          {ACTIONS.abandon.button}
        </button>
      )}

      {/* Löschen geht immer. Ein Turnier, das nicht hätte entstehen sollen, ist
          in jedem Zustand eines — auch im abgeschlossenen, denn genau so endet
          ein Probelauf. */}
      <button type="button" className="md-btn" onClick={() => setConfirming('delete')}>
        {ACTIONS.delete.button}
      </button>
    </div>
  )
}
