import { PageHeader } from '../components/layout/PageHeader'
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
      <PageHeader
        title="Meine Turniere"
        tag={me ? (me.isSystemAdmin ? 'systemadmin' : 'veranstalter') : '—'}
        subtitle={
          tournaments.length === 0
            ? 'Noch kein Turnier ausgeschrieben'
            : `${tournaments.length} ${tournaments.length === 1 ? 'Turnier' : 'Turniere'}`
        }
      >
        <button type="button" className="md-btn md-btn--accent" onClick={onCreate}>
          Turnier anlegen
        </button>
      </PageHeader>

      <section className="md-section">
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
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
              gap: 'var(--sp-5)',
            }}
          >
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
    <button
      type="button"
      onClick={onOpen}
      style={{
        textAlign: 'left',
        cursor: 'pointer',
        padding: 'var(--sp-8)',
        borderRadius: 'var(--radius-md)',
        background: active ? 'var(--surface-chosen)' : 'var(--surface)',
        border: active ? '1px solid var(--blue-400)' : 'var(--border)',
        boxShadow: active ? 'var(--shadow-sm)' : 'none',
        fontFamily: 'inherit',
        display: 'flex',
        flexDirection: 'column',
        gap: 7,
      }}
    >
      <div style={{ fontSize: 'var(--fs-lg)', fontWeight: 'var(--fw-bold)', lineHeight: 'var(--lh-snug)' }}>
        {entry.name}
      </div>

      <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--fg-2)' }}>
        {entry.venueName} · {disciplineLabel[entry.discipline]}
      </div>

      <div className="md-num" style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
        {formatDateRange(entry.startsOn, entry.endsOn)}
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--sp-4)',
          marginTop: 'var(--sp-4)',
          flexWrap: 'wrap',
        }}
      >
        <span className="md-pill" aria-pressed={false} style={{ pointerEvents: 'none' }}>
          {tournamentStateLabel[entry.state]}
        </span>
        <span style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
          {entry.state === TournamentState.Draft
            ? 'noch keine Meldungen'
            : `${entry.acceptedEntries} im Feld`}
        </span>
      </div>
    </button>
  )
}
