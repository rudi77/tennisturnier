import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import type { LiveAction, MatchView, ResultRequest, TournamentView } from '../api'
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
  onLive,
  onSave,
  onClear,
}: {
  view: TournamentView
  matchId: string
  onClose: () => void
  onLive: (action: LiveAction, side?: 1 | 2) => Promise<void>
  onSave: (result: ResultRequest) => Promise<void>
  onClear: () => Promise<void>
}) {
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [whole, setWhole] = useState(false)

  // Immer der Stand aus der aktuellen Sicht: Trägt ein anderes Handy ein,
  // kommt die neue Sicht über die Live-Verbindung und steht sofort hier.
  const match = view.matches.find((m) => m.id === matchId)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !whole && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose, whole])

  if (!match) return null

  if (whole) {
    return <ResultEditor view={view} match={match} onClose={() => setWhole(false)} onSave={onSave} onClear={onClear} />
  }

  async function step(action: LiveAction, side?: 1 | 2) {
    setBusy(true)
    setError(null)
    try {
      await onLive(action, side)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const live = match.live ?? null
  const finished = match.status === 'Finished'
  const counted = (match.liveEvents ?? 0) > 0
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
                  <button type="button" className="button button--primary live-scorer__point" disabled={busy} onClick={() => void step('Point', side)} aria-label={`Punkt für ${name}`}>
                    <span className="live-scorer__plus">+1</span>
                    <span className="live-scorer__name">{name}</span>
                  </button>
                  {!tiebreak && (
                    <button type="button" className="button live-scorer__game" disabled={busy} onClick={() => void step('Game', side)} aria-label={`Spiel für ${name}`}>
                      Spiel für {name}
                    </button>
                  )}
                </div>
              )
            })}
          </div>
        )}

        {error && <p className="error">{error}</p>}

        <div className="actions">
          <button type="button" className="button" disabled={busy || !counted} onClick={() => void step('Undo')}>
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
