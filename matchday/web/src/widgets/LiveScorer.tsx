import { useEffect, useReducer, useState } from 'react'
import { createPortal } from 'react-dom'
import type { LiveAction, MatchView, ResultRequest, Scored, TournamentView } from '../api'
import { tick, useWakeLock } from '../device'
import { outbox } from '../outbox'
import { setColumns } from '../format'
import { ResultEditor } from './ResultEditor'

/**
 * Mitzählen, während gespielt wird: je Seite ein großer Knopf für den Punkt
 * und ein kleiner für ein ganzes Spiel, dazu Rückgängig. Der Stand kommt vom
 * Server — auch was andere gerade eintragen, steht sofort hier. Ist das Match
 * entschieden, steht das Ergebnis von selbst.
 *
 * Wer nicht Punkt für Punkt zählen will, trägt über „Ganzes Ergebnis“ die
 * Sätze auf einmal ein — dieselbe Maske wie bisher.
 */
export function LiveScorer({
  view,
  matchId,
  onClose,
  apply,
  onSave,
  onClear,
}: {
  view: TournamentView
  matchId: string
  onClose: () => void
  /** Ein angekommener Schritt: die Antwort mit dem neuen Stand. */
  apply: (scored: Scored) => void
  onSave: (result: ResultRequest) => Promise<void>
  onClear: () => Promise<void>
}) {
  const [whole, setWhole] = useState(false)
  const [, neu] = useReducer((n: number) => n + 1, 0)

  // Die Warteschlange meldet, was ankam und was noch aussteht. Angekommenes
  // dieses Turniers wird gleich übernommen; der Live-Kanal brächte es auch,
  // nur etwas später.
  useEffect(
    () =>
      outbox.subscribe({
        applied: (scored) => scored.tournament.id === view.id && apply(scored),
        changed: neu,
      }),
    [apply, view.id],
  )

  // Immer der Stand aus der aktuellen Sicht: Trägt ein anderes Handy ein,
  // kommt die neue Sicht über die Live-Verbindung und steht sofort hier.
  const match = view.matches.find((m) => m.id === matchId)

  // Solange gezählt werden kann, bleibt der Bildschirm an.
  useWakeLock(match !== undefined && match.status !== 'Finished')

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !whole && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose, whole])

  if (!match) return null

  if (whole) {
    return <ResultEditor view={view} match={match} onClose={() => setWhole(false)} onSave={onSave} onClear={onClear} />
  }

  // Kein Warten auf den Server: Der Schritt steht an, und die Warteschlange
  // schickt ihn, sobald sie kann. So bleibt das Zählen auch ohne Netz flüssig.
  function step(action: LiveAction, side?: 1 | 2) {
    tick()
    outbox.push({ tournamentId: view.id, matchId, action, side }, match!.liveEvents ?? 0)
    neu()
  }

  const pending = outbox.pending(matchId)
  const error = outbox.meldungen.get(matchId) ?? null

  const live = match.live ?? null
  const finished = match.status === 'Finished'
  const counted = (match.liveEvents ?? 0) + pending > 0
  // Ein Ergebnis, das nicht live entstand, lässt sich nicht weiterzählen —
  // erst über „Ganzes Ergebnis“ löschen.
  const locked = finished || (match.score !== null && !counted)
  const tiebreak = live?.inTiebreak || live?.inMatchTiebreak

  // Ins Dokument selbst gehängt: Die Bühne ist ein eigener Stapel, und darin
  // läge das Fenster unter Gespräch und Eingabefeld.
  return createPortal(
    <div className="modal" role="dialog" aria-modal="true" aria-label={`Live: ${match.label}`} onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="modal__box live-scorer">
        <div className="card__head">
          <h2 className="card__title">{match.label}</h2>
          <button type="button" className="icon" aria-label="Schließen" onClick={onClose}>
            ×
          </button>
        </div>
        <p className="muted">{view.formatText}</p>

        <Scoreboard match={match} />

        {finished ? (
          <p className="live-scorer__state">
            Beendet — Sieg für {match.score?.winnerSide === 1 ? match.side1.name : match.side2.name}
          </p>
        ) : live ? (
          <p className="live-scorer__state">
            {live.inMatchTiebreak ? 'Match-Tiebreak' : live.inTiebreak ? 'Tiebreak' : 'Es wird gespielt'}
          </p>
        ) : (
          <p className="live-scorer__state">Noch kein Punkt gezählt.</p>
        )}

        {!locked && (
          <div className="live-scorer__sides">
            {([1, 2] as const).map((side) => {
              const name = side === 1 ? match.side1.name : match.side2.name
              return (
                <div key={side} className="live-scorer__side">
                  <button type="button" className="button button--primary live-scorer__point" onClick={() => step('Point', side)} aria-label={`Punkt für ${name}`}>
                    <span className="live-scorer__plus">+1</span>
                    <span className="live-scorer__name">{name}</span>
                  </button>
                  {!tiebreak && (
                    <button type="button" className="button live-scorer__game" onClick={() => step('Game', side)} aria-label={`Spiel für ${name}`}>
                      Spiel für {name}
                    </button>
                  )}
                </div>
              )
            })}
          </div>
        )}

        {pending > 0 && (
          <p className={`live-scorer__pending${outbox.offline ? ' live-scorer__pending--offline' : ''}`} role="status">
            {outbox.offline
              ? `Kein Netz — ${pending === 1 ? 'eine Eingabe wartet' : `${pending} Eingaben warten`} und wird nachgeschickt.`
              : `${pending === 1 ? 'Eine Eingabe wird' : `${pending} Eingaben werden`} gesendet …`}
          </p>
        )}
        {error && <p className="error">{error}</p>}

        <div className="actions">
          <button type="button" className="button" disabled={!counted} onClick={() => step('Undo')}>
            ↶ Rückgängig
          </button>
          <span className="spacer" />
          <button type="button" className="button button--quiet" onClick={() => setWhole(true)}>
            Ganzes Ergebnis
          </button>
          <button type="button" className="button" onClick={onClose}>
            Fertig
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}

/** Die Anzeigetafel: je Seite Name, Sätze und — solange gespielt wird — die Punkte des Spiels. */
export function Scoreboard({ match }: { match: MatchView }) {
  const live = !match.score ? (match.live ?? null) : null

  return (
    <table className="scoreboard">
      <tbody>
        {([1, 2] as const).map((side) => {
          const name = side === 1 ? match.side1.name : match.side2.name
          const columns = setColumns(match, side)
          const winner = match.score?.winnerSide === side
          return (
            <tr key={side} className={winner ? 'winner' : ''}>
              <th scope="row" className="scoreboard__name">
                {name}
              </th>
              {columns.map((c, i) => (
                <td key={i} className={`scoreboard__set${c.won ? ' scoreboard__set--won' : ''}`}>
                  {c.games}
                  {c.tiebreak !== null && !c.won && <sup>{c.tiebreak}</sup>}
                </td>
              ))}
              {live && !live.inMatchTiebreak && <td className="scoreboard__points">{side === 1 ? live.points1 : live.points2}</td>}
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}
