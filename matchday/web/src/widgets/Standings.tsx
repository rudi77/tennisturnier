import type { MatchView, TournamentView } from '../api'
import { MatchCard } from './Bracket'

/** Jeder gegen jeden: die Tabelle, darunter die Runden mit ihren Matches. */
export function Standings({ view, onOpen, embedded = false }: { view: TournamentView; onOpen: ((match: MatchView) => void) | null; embedded?: boolean }) {
  const rounds = Array.from({ length: view.rounds }, (_, i) => i + 1)
  const body = (
    <>
      <div className="table-wrap">
        <table className="standings">
          <thead>
            <tr>
              <th>#</th>
              <th className="left">Name</th>
              <th title="Spiele">Sp.</th>
              <th title="Siege">S</th>
              <th title="Niederlagen">N</th>
              <th>Sätze</th>
              <th>Spiele</th>
            </tr>
          </thead>
          <tbody>
            {view.standings.map((s) => (
              <tr key={s.participantId}>
                <td>{s.rank}</td>
                <td className="left">{s.name}</td>
                <td>{s.played}</td>
                <td>{s.won}</td>
                <td>{s.lost}</td>
                <td>
                  {s.setsWon}:{s.setsLost}
                </td>
                <td>
                  {s.gamesWon}:{s.gamesLost}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="rounds">
        {rounds.map((round) => (
          <div key={round} className="rounds__round">
            <h3 className="bracket__title">Runde {round}</h3>
            <div className="rounds__matches">
              {view.matches
                .filter((m) => m.round === round)
                .map((m) => (
                  <MatchCard key={m.id} match={m} onOpen={onOpen} />
                ))}
            </div>
          </div>
        ))}
      </div>
    </>
  )
  return embedded ? <div className="card__section">{body}</div> : <section className="card">{body}</section>
}
