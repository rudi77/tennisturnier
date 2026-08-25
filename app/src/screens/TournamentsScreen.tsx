import { ScreenHeader } from '../components/layout/ScreenHeader'
import { Empty, Loading } from '../components/layout/StateBlock'
import { useWorkspace } from '../state/WorkspaceContext'
import { disciplineLabel, tournamentStateLabel } from '../lib/labels'
import { formatDateRange } from '../lib/time'
import { TournamentState, type TournamentSummary } from '../api/types'

/**
 * Der Einstieg: die eigenen Turniere.
 *
 * Hier stand einmal ein Verein, den jemand anlegen musste, bevor irgendetwas
 * ging. Er ist ersatzlos entfallen — wer sich anmeldet, darf ausschreiben, und
 * wer ausschreibt, führt sein Turnier. Diese Liste ist deshalb die erste
 * Antwort auf die Frage „was habe ich hier zu tun".
 *
 * Was darin steht, entscheidet der Query-Filter und nicht diese Ansicht: ein
 * Turnier sieht nur, wer eine Rolle daran hat.
 */
export function TournamentsScreen({
  onCreate,
  onOpen,
}: {
  onCreate: () => void
  onOpen: () => void
}) {
  const { tournaments, tournament, selectTournament, loading, me } = useWorkspace()

  const open = (id: string) => {
    selectTournament(id)
    onOpen()
  }

  return (
    <>
      <section className="md-section">
        <ScreenHeader
          title="Meine Turniere"
          lead={
            tournaments.length === 0
              ? 'Noch kein Turnier ausgeschrieben.'
              : `${tournaments.length} ${tournaments.length === 1 ? 'Turnier' : 'Turniere'}` +
                (me?.isSystemAdmin ? ' · Systemadministrator' : '')
          }
        >
          <button type="button" className="md-btn md-btn--accent md-btn--wide" onClick={onCreate}>
            Turnier anlegen
          </button>
        </ScreenHeader>
        {loading && tournaments.length === 0 ? (
          <Loading label="Turniere werden geladen …" />
        ) : tournaments.length === 0 ? (
          <Empty
            title="Noch kein Turnier"
            hint={
              'Ein Turnier braucht einen Namen, einen Ort und eine Disziplin — mehr nicht. ' +
              'Termin, Plätze und Meldungen kommen danach.'
            }
          />
        ) : (
          <div className="md-cardlist">
            {tournaments.map((entry) => (
              <Card
                key={entry.id}
                entry={entry}
                active={entry.id === tournament?.id}
                onOpen={() => open(entry.id)}
              />
            ))}
          </div>
        )}
      </section>
    </>
  )
}

function Card({
  entry,
  active,
  onOpen,
}: {
  entry: TournamentSummary
  active: boolean
  onOpen: () => void
}) {
  return (
    <button type="button" className="md-card" aria-current={active ? 'true' : undefined} onClick={onOpen}>
      <span className="md-card__title">{entry.name}</span>

      <span className="md-card__meta">
        {entry.venueName} · {disciplineLabel[entry.discipline]}
      </span>

      <span className="md-card__meta md-num">
        {formatDateRange(entry.startsOn, entry.endsOn)}
      </span>

      <span className="md-card__foot">
        <span className="md-chip">{tournamentStateLabel[entry.state]}</span>
        <span className="md-card__meta">
          {entry.state === TournamentState.Draft
            ? 'noch keine Meldungen'
            : `${entry.acceptedEntries} im Feld`}
        </span>
      </span>
    </button>
  )
}
