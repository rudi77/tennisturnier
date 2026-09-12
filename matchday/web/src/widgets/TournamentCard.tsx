import type { TournamentView } from '../api'
import { dateText, modeText, stateText } from '../format'
import type { Act } from './Widget'
import { ParticipantList } from './ParticipantList'
import { Bracket } from './Bracket'
import { Standings } from './Standings'
import type { MatchView } from '../api'

export function TournamentHeader({ view }: { view: TournamentView }) {
  const meta = [dateText(view.date), view.location, modeText(view.mode), view.formatText].filter(Boolean)
  return (
    <header className="tournament">
      <div className="tournament__row">
        <h1 className="tournament__name">{view.name}</h1>
        <span className={`state state--${view.state.toLowerCase()}`}>{stateText(view.state)}</span>
      </div>
      <p className="tournament__meta">{meta.join(' · ')}</p>
    </header>
  )
}

/** Die Turnierkarte: Rahmen oben, darunter das, was gerade zählt. */
export function TournamentCard({
  view,
  admin,
  act,
  onOpen,
}: {
  view: TournamentView
  admin: boolean
  act: Act
  onOpen: ((match: MatchView) => void) | null
}) {
  return (
    <section className="card card--tournament">
      <TournamentHeader view={view} />
      {view.state === 'Setup' ? (
        <ParticipantList view={view} admin={admin} act={act} embedded />
      ) : view.mode === 'Knockout' ? (
        <Bracket view={view} onOpen={onOpen} embedded />
      ) : (
        <Standings view={view} onOpen={onOpen} embedded />
      )}
    </section>
  )
}
