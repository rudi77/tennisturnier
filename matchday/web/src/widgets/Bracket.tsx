import type { MatchView, TournamentView } from '../api'
import { roundName, setColumns } from '../format'

/**
 * Der Baum: eine Spalte je Runde. Anklickbar ist nur, was eingetragen werden
 * kann — beide Seiten stehen, kein Freilos — und nur mit Verwalterrecht.
 */
export function Bracket({ view, onOpen, embedded = false }: { view: TournamentView; onOpen: ((match: MatchView) => void) | null; embedded?: boolean }) {
  const rounds = Array.from({ length: view.rounds }, (_, i) => i + 1)
  const body = (
    <div className="bracket" role="list">
      {rounds.map((round) => (
        <div key={round} className="bracket__round">
          <h3 className="bracket__title">{roundName(view, round)}</h3>
          <div className="bracket__matches">
            {view.matches
              .filter((m) => m.round === round)
              .map((m) => (
                <MatchCard key={m.id} match={m} onOpen={onOpen} />
              ))}
          </div>
        </div>
      ))}
    </div>
  )
  return embedded ? <div className="card__section card__section--flush">{body}</div> : <section className="card card--flush">{body}</section>
}

export function MatchCard({ match, onOpen, showLabel = false }: { match: MatchView; onOpen: ((match: MatchView) => void) | null; showLabel?: boolean }) {
  const clickable = onOpen !== null && !match.isBye && match.status !== 'Pending'
  const winner = match.score?.winnerSide ?? null
  const playing = match.status === 'Playing'
  const className = ['match', match.status === 'Finished' ? 'match--finished' : '', playing ? 'match--playing' : '', match.isBye ? 'match--bye' : '', clickable ? 'match--clickable' : '']
    .filter(Boolean)
    .join(' ')

  return (
    <button type="button" className={className} disabled={!clickable} onClick={() => onOpen?.(match)} role="listitem" aria-label={match.label}>
      {showLabel && <span className="match__label">{match.label}</span>}
      {playing && (
        <span className="match__live" aria-label="Läuft gerade">
          <span className="live__dot" /> live
        </span>
      )}
      <Side match={match} side={1} winner={winner === 1} />
      <Side match={match} side={2} winner={winner === 2} />
      {match.score && match.score.outcome !== 'Normal' && match.score.outcome !== 'Bye' && (
        <span className="match__note">{match.score.outcome === 'Walkover' ? 'kampflos' : 'Aufgabe'}</span>
      )}
    </button>
  )
}

function Side({ match, side, winner }: { match: MatchView; side: 1 | 2; winner: boolean }) {
  const s = side === 1 ? match.side1 : match.side2
  const open = s.kind !== 'Participant'
  const columns = setColumns(match, side)
  // Solange gespielt wird, steht hinter den Sätzen der Punktestand des Spiels.
  const live = !match.score && match.live && !match.live.inMatchTiebreak ? match.live : null
  return (
    <span className={`side${winner ? ' side--winner' : ''}${open ? ' side--open' : ''}`}>
      <span className="side__name">{s.name}</span>
      <span className="side__sets">
        {columns.map((c, i) => (
          <span key={i} className={`set${c.won ? ' set--won' : ''}`}>
            {c.games}
            {c.tiebreak !== null && !c.won && <sup>{c.tiebreak}</sup>}
          </span>
        ))}
        {live && <span className="set set--points">{side === 1 ? live.points1 : live.points2}</span>}
      </span>
    </span>
  )
}
