import { useMemo, useState } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { TournamentPicker } from '../components/layout/TournamentPicker'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { MatchdayMark } from '../components/core/MatchdayMark'
import { StatusChip } from '../components/core/StatusChip'
import { usePublicView } from '../hooks/usePublicView'
import { useWorkspaceOptional } from '../state/WorkspaceContext'
import { publicAssignmentStatusLabel, publicAssignmentTone } from '../lib/labels'
import { formatClock } from '../lib/time'
import type { PublicMatchView, PublicTournamentView } from '../api/types'

/**
 * Die öffentliche Live-Ansicht.
 *
 * Sie zeigt nur, was die Projektion hergibt — keine Kontaktdaten, keine
 * Geburtsdaten, keine internen Notizen zu Sperren und keine Ids von Personen.
 * Der `TournamentViewBuilder` im Backend entscheidet das allein; diese Seite
 * reichert nichts an, weil jede Anreicherung eine zweite Stelle wäre, an der
 * Daten öffentlich werden.
 */
export function PublicScreen({ standalone = false }: { standalone?: boolean }) {
  const workspace = useWorkspaceOptional()
  const [device, setDevice] = useState<'phone' | 'kiosk'>('phone')

  // Ohne Anmeldung kommt die Turnier-Id aus der Adresszeile: ?t=<guid>. Der
  // Aushang im Vereinsheim ist genau das — ein Link, kein angemeldeter Client.
  const fromQuery = useMemo(() => new URLSearchParams(window.location.search).get('t'), [])
  const tournamentId = standalone ? fromQuery : (workspace?.tournament?.id ?? null)
  const timeZone = workspace?.timeZone ?? 'Europe/Vienna'

  const state = usePublicView(tournamentId)

  return (
    <>
      <PageHeader
        title="Öffentliche Live-Ansicht"
        tag="/public"
        subtitle="Read-Modell · ETag + SignalR · datensparsam"
        kpis={[
          { value: state.live ? 'live' : 'poll', label: 'Kanal', color: state.live ? 'var(--court-900)' : 'var(--fg-3)' },
          { value: state.notModifiedCount, label: '304', color: 'var(--fg-3)' },
        ]}
      >
        {!standalone && <TournamentPicker />}
      </PageHeader>

      <section className="md-section">
        <div
          style={{
            display: 'flex',
            gap: 'var(--sp-6)',
            alignItems: 'center',
            flexWrap: 'wrap',
            marginBottom: 'var(--sp-10)',
          }}
        >
          <div className="md-pillbar">
            <button
              type="button"
              className="md-seg"
              aria-pressed={device === 'phone'}
              onClick={() => setDevice('phone')}
            >
              Handy
            </button>
            <button
              type="button"
              className="md-seg"
              aria-pressed={device === 'kiosk'}
              onClick={() => setDevice('kiosk')}
            >
              Clubhaus-Monitor
            </button>
          </div>
          <div className="md-hint" style={{ maxWidth: 560 }}>
            Öffentliche Projektion — ohne Anmeldung, mit <code className="md-num">ETag</code> und
            15 s Cache. Der Push über <code className="md-num">/hubs/tournament</code> trägt nur Id
            und ETag; geholt wird über denselben Endpunkt wie beim Polling.
          </div>
        </div>

        {!tournamentId ? (
          <Empty
            title="Kein Turnier"
            hint={
              standalone
                ? 'Die Adresse braucht die Turnier-Id: ?t=<guid>.'
                : 'Kein Turnier ausgewählt.'
            }
          />
        ) : state.error ? (
          <ErrorBlock error={state.error} onRetry={() => void state.reload()} />
        ) : state.loading && !state.view ? (
          <Loading label="Projektion wird geholt …" />
        ) : !state.view ? (
          <Empty
            title="Noch keine öffentliche Ansicht"
            hint="Vor der Auslosung gibt es keine Projektion — und eine zurückgenommene Auslosung lässt sie wieder verschwinden."
          />
        ) : device === 'phone' ? (
          <PhoneView view={state.view} etag={state.etag} timeZone={timeZone} live={state.live} />
        ) : (
          <KioskView view={state.view} timeZone={timeZone} />
        )}
      </section>
    </>
  )
}

function partition(view: PublicTournamentView) {
  const all = view.phases.flatMap((phase) => phase.matches)
  return {
    all,
    live: all.filter(
      (match) => match.assignmentStatus === 'Running' || match.assignmentStatus === 'Suspended',
    ),
    next: all.filter(
      (match) => match.assignmentStatus === 'Planned' || match.assignmentStatus === 'Called',
    ),
    results: all.filter((match) => match.status === 'Finished' && match.score),
  }
}

function pairOf(match: PublicMatchView): string {
  return `${match.side1.name ?? match.side1.origin} vs ${match.side2.name ?? match.side2.origin}`
}

function winnerLine(match: PublicMatchView): string {
  if (!match.winnerSide) return pairOf(match)
  const winner = match.winnerSide === 1 ? match.side1 : match.side2
  const loser = match.winnerSide === 1 ? match.side2 : match.side1
  return `${winner.name ?? winner.origin} d. ${loser.name ?? loser.origin}`
}

function PhoneView({
  view,
  etag,
  timeZone,
  live,
}: {
  view: PublicTournamentView
  etag: string | null
  timeZone: string
  live: boolean
}) {
  const { live: running, next, results } = partition(view)

  return (
    <div style={{ display: 'flex', gap: 'var(--sp-12)', flexWrap: 'wrap', alignItems: 'flex-start' }}>
      <div className="md-phone">
        <div style={{ background: 'var(--surface-muted)', borderRadius: 28, overflow: 'hidden' }}>
          <div
            style={{
              background: 'var(--court-950)',
              color: 'var(--fg-on-dark)',
              padding: '9px 20px 12px',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
            }}
          >
            <span className="md-num" style={{ fontSize: 'var(--fs-xs)' }}>
              {formatClock(new Date().toISOString(), timeZone)}
            </span>
            <div style={{ width: 74, height: 19, borderRadius: 'var(--radius-pill)', background: '#000' }} />
            <span className="md-num" style={{ fontSize: 'var(--fs-xs)' }}>
              5G ▮
            </span>
          </div>

          <div style={{ background: 'var(--court-950)', color: 'var(--fg-on-dark)', padding: '2px 18px 16px' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-4)' }}>
              <MatchdayMark size={17} solid />
              <span style={{ fontSize: 'var(--fs-md)', fontWeight: 'var(--fw-bold)' }}>{view.name}</span>
            </div>
            <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-on-dark-2)', marginTop: 4 }}>
              {view.clubName} · {view.startsOn} – {view.endsOn}
            </div>
          </div>

          <div
            style={{
              padding: 'var(--sp-7)',
              display: 'flex',
              flexDirection: 'column',
              gap: 'var(--sp-6)',
              maxHeight: 640,
              overflowY: 'auto',
            }}
          >
            <section>
              <div style={{ display: 'flex', alignItems: 'center', gap: 7, marginBottom: 'var(--sp-4)' }}>
                {live && <span className="md-live-dot" />}
                <span className="md-eyebrow" style={{ color: 'var(--fg)' }}>
                  Jetzt am Platz
                </span>
                <span
                  className="md-num"
                  style={{ fontSize: 10.5, color: 'var(--fg-3)', marginLeft: 'auto' }}
                >
                  {running.length} Matches
                </span>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-4)' }}>
                {running.length === 0 && (
                  <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
                    Gerade läuft nichts.
                  </div>
                )}
                {running.map((match) => (
                  <div
                    key={match.id}
                    style={{
                      background: 'var(--surface)',
                      border: 'var(--border)',
                      borderLeft: '3px solid var(--acc)',
                      borderRadius: 'var(--radius-md)',
                      padding: '11px var(--sp-6)',
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 8 }}>
                      <span
                        style={{
                          fontSize: 10.5,
                          fontWeight: 'var(--fw-bold)',
                          letterSpacing: '0.05em',
                          color: 'var(--court-700)',
                        }}
                      >
                        {match.courtName ?? '—'}
                      </span>
                      <span className="md-num" style={{ fontSize: 10, color: 'var(--fg-3)' }}>
                        {match.label ?? ''}
                      </span>
                    </div>
                    <SideLine name={match.side1.name ?? match.side1.origin} />
                    <SideLine name={match.side2.name ?? match.side2.origin} />
                    {match.score && (
                      <div
                        className="md-num"
                        style={{ fontSize: 'var(--fs-sm)', fontWeight: 'var(--fw-semibold)', marginTop: 6 }}
                      >
                        {match.score}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </section>

            <section>
              <div className="md-eyebrow" style={{ color: 'var(--fg)', marginBottom: 'var(--sp-4)' }}>
                Als nächstes
              </div>
              <div style={{ background: 'var(--surface)', border: 'var(--border)', borderRadius: 'var(--radius-md)', overflow: 'hidden' }}>
                {next.slice(0, 6).map((match) => (
                  <div
                    key={match.id}
                    style={{
                      padding: 'var(--sp-5) var(--sp-6)',
                      borderBottom: '1px solid var(--line-hairline)',
                      display: 'flex',
                      gap: 'var(--sp-5)',
                      alignItems: 'center',
                    }}
                  >
                    <div style={{ width: 54, flex: 'none' }}>
                      {match.earliestStart ? (
                        <div className="md-time md-time--promise">
                          {formatClock(match.earliestStart, timeZone)}
                        </div>
                      ) : (
                        <div className="md-time md-time--estimate">
                          ~{formatClock(match.plannedStart, timeZone)}
                        </div>
                      )}
                      <div style={{ fontSize: 9.5, color: 'var(--fg-3)', marginTop: 1 }}>
                        {match.courtName ?? '—'}
                      </div>
                    </div>
                    <div style={{ flex: 1, minWidth: 0, fontSize: 11.5, fontWeight: 'var(--fw-semibold)', lineHeight: 1.4 }}>
                      {pairOf(match)}
                    </div>
                    <StatusChip
                      tone={match.earliestStart ? 'planned' : 'finished'}
                      style={{ fontSize: 9 }}
                    >
                      {match.earliestStart ? 'Zusage' : 'Schätzung'}
                    </StatusChip>
                  </div>
                ))}
                {next.length === 0 && (
                  <div style={{ padding: 'var(--sp-6)', fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
                    Keine weiteren Ansetzungen.
                  </div>
                )}
              </div>
              <div style={{ fontSize: 10, color: 'var(--fg-3)', marginTop: 7, lineHeight: 'var(--lh-normal)' }}>
                Fette Zeiten sind Zusagen („nicht vor"). Kursive Zeiten mit ~ sind Schätzungen und
                verschieben sich mit dem Spielverlauf.
              </div>
            </section>

            <section>
              <div className="md-eyebrow" style={{ color: 'var(--fg)', marginBottom: 'var(--sp-4)' }}>
                Letzte Ergebnisse
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
                {results.slice(-6).reverse().map((match) => (
                  <div
                    key={match.id}
                    style={{
                      background: 'var(--surface)',
                      border: 'var(--border)',
                      borderRadius: 'var(--radius)',
                      padding: '8px 11px',
                      display: 'flex',
                      gap: 'var(--sp-4)',
                      alignItems: 'center',
                    }}
                  >
                    <div style={{ flex: 1, minWidth: 0, fontSize: 11.5, lineHeight: 1.4 }}>
                      {winnerLine(match)}
                      {match.outcome && match.outcome !== 'Normal' && (
                        <span style={{ color: 'var(--fg-3)' }}> · {match.outcome}</span>
                      )}
                    </div>
                    <span
                      className="md-num"
                      style={{ fontSize: 'var(--fs-xs)', fontWeight: 'var(--fw-semibold)', flex: 'none' }}
                    >
                      {match.score ?? ''}
                    </span>
                  </div>
                ))}
                {results.length === 0 && (
                  <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)' }}>
                    Noch kein Ergebnis.
                  </div>
                )}
              </div>
            </section>
          </div>

          <div style={{ display: 'flex', borderTop: 'var(--border)', background: 'var(--surface)' }}>
            {['Live', 'Draw', 'Plätze', 'Info'].map((label, index) => (
              <div
                key={label}
                style={{
                  flex: 1,
                  textAlign: 'center',
                  padding: '12px 0',
                  fontSize: 10.5,
                  fontWeight: index === 0 ? 'var(--fw-bold)' : 'var(--fw-semibold)',
                  color: index === 0 ? 'var(--fg)' : 'var(--fg-3)',
                  borderTop: index === 0 ? '2px solid var(--acc)' : '2px solid transparent',
                  minHeight: 'var(--hit-target)',
                }}
              >
                {label}
              </div>
            ))}
          </div>
        </div>
      </div>

      <div className="md-panel" style={{ flex: 1, minWidth: 300, maxWidth: 460, padding: 18 }}>
        <div style={{ fontSize: 'var(--fs-md)', fontWeight: 'var(--fw-bold)', marginBottom: 'var(--sp-5)' }}>
          Projektion · GET /public/tournaments/{'{id}'}
        </div>
        <pre className="md-snapshot" style={{ maxHeight: 420, overflowY: 'auto' }}>
          {JSON.stringify(
            {
              etag,
              id: view.id,
              name: view.name,
              clubName: view.clubName,
              state: view.state,
              schedulingMode: view.schedulingMode,
              courts: view.courts.map((court) => ({
                name: court.name,
                queue: court.queue.length,
                status: court.queue[0]?.status ?? null,
              })),
              phases: view.phases.map((phase) => ({
                name: phase.name,
                status: phase.status,
                matches: phase.matches.length,
              })),
            },
            null,
            2,
          )}
        </pre>
        <div style={{ fontSize: 11.5, color: 'var(--fg-2)', lineHeight: 'var(--lh-relaxed)', marginTop: 'var(--sp-6)' }}>
          Der Mapper ist die einzige Stelle, die Datensparsamkeit durchsetzt. Ein zweiter Abruf mit{' '}
          <code className="md-num">If-None-Match</code> bekommt 304.
        </div>
      </div>
    </div>
  )
}

function SideLine({ name }: { name: string }) {
  return (
    <div
      style={{
        fontSize: 'var(--fs-sm)',
        marginTop: 4,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        color: 'var(--fg-2)',
      }}
    >
      {name}
    </div>
  )
}

/** Der Aushang im Vereinsheim: aus vier Metern lesbar, ohne Bedienung. */
function KioskView({ view, timeZone }: { view: PublicTournamentView; timeZone: string }) {
  const { all, results } = partition(view)
  const byId = new Map(all.map((match) => [match.id, match]))

  return (
    <div className="md-kiosk">
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--sp-7)',
          flexWrap: 'wrap',
          marginBottom: 22,
        }}
      >
        <MatchdayMark size={30} />
        <div>
          <div style={{ fontSize: 'var(--fs-2xl)', fontWeight: 'var(--fw-bold)', lineHeight: 'var(--lh-tight)' }}>
            {view.name}
          </div>
          <div style={{ fontSize: 'var(--fs-md)', color: 'var(--fg-on-dark-2)', marginTop: 3 }}>
            {view.clubName} · {view.state}
          </div>
        </div>
        <div style={{ marginLeft: 'auto', textAlign: 'right' }}>
          <div className="md-num" style={{ fontSize: 'var(--fs-clock)', fontWeight: 'var(--fw-semibold)', lineHeight: 1 }}>
            {formatClock(new Date().toISOString(), timeZone)}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 7, justifyContent: 'flex-end', marginTop: 5 }}>
            <span className="md-live-dot" />
            <span
              style={{
                fontSize: 'var(--fs-xs)',
                letterSpacing: 'var(--ls-wider)',
                textTransform: 'uppercase',
                color: 'var(--fg-on-dark-2)',
                fontWeight: 'var(--fw-semibold)',
              }}
            >
              Live
            </span>
          </div>
        </div>
      </div>

      <div className="md-kiosk__grid">
        {view.courts.map((court) => {
          // „Aufgerufen" gehört auf den Aushang wie „läuft": ein Spieler muss
          // gerade zum Platz. Ein Platz, der dann „frei" zeigt, schickt ihn weg.
          const isCurrent = (status: string) =>
            status === 'Running' || status === 'Called' || status === 'Suspended'
          const currentSlot = court.queue.find((slot) => isCurrent(slot.status)) ?? null
          const current = currentSlot ? byId.get(currentSlot.matchId) ?? null : null
          const nextSlot = court.queue.find((slot) => slot !== currentSlot)
          const next = nextSlot ? byId.get(nextSlot.matchId) ?? null : null
          const running = currentSlot?.status === 'Running'

          return (
            <div
              key={court.id}
              style={{
                background: running ? 'var(--ball-tint)' : 'rgba(255,255,255,.04)',
                border: running ? '1px solid var(--acc)' : '1px solid var(--line-on-dark)',
                borderRadius: 'var(--radius-lg)',
                padding: '14px var(--sp-8)',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-4)' }}>
                <span
                  style={{
                    fontSize: 'var(--fs-md)',
                    fontWeight: 'var(--fw-bold)',
                    letterSpacing: '0.04em',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {court.name}
                </span>
                <StatusChip
                  tone={currentSlot ? publicAssignmentTone(currentSlot.status) : 'finished'}
                >
                  {currentSlot ? publicAssignmentStatusLabel[currentSlot.status] : 'frei'}
                </StatusChip>
              </div>

              <KioskSide name={current ? current.side1.name ?? current.side1.origin : '—'} />
              <KioskSide name={current ? current.side2.name ?? current.side2.origin : '—'} />

              {current?.score && (
                <div
                  className="md-num"
                  style={{ fontSize: 17, fontWeight: 'var(--fw-semibold)', marginTop: 8 }}
                >
                  {current.score}
                </div>
              )}

              <div
                style={{
                  marginTop: 'var(--sp-6)',
                  paddingTop: 'var(--sp-5)',
                  borderTop: '1px solid var(--line-on-dark)',
                  fontSize: 'var(--fs-xs)',
                  color: 'var(--fg-on-dark-2)',
                  lineHeight: 1.4,
                }}
              >
                {next
                  ? `Danach ${
                      nextSlot?.earliestStart
                        ? `ab ${formatClock(nextSlot.earliestStart, timeZone)}`
                        : nextSlot?.plannedStart
                          ? `~${formatClock(nextSlot.plannedStart, timeZone)}`
                          : ''
                    } · ${pairOf(next)}`
                  : 'Keine weiteren Matches'}
              </div>
            </div>
          )
        })}
      </div>

      <div
        style={{
          marginTop: 'var(--sp-10)',
          paddingTop: 'var(--sp-8)',
          borderTop: '1px solid var(--line-on-dark)',
          display: 'flex',
          gap: 'var(--sp-14)',
          overflow: 'hidden',
          whiteSpace: 'nowrap',
        }}
      >
        {results.slice(-6).reverse().map((match) => (
          <div key={match.id} style={{ fontSize: 12.5, color: 'var(--fg-on-dark-2)' }}>
            {winnerLine(match)}{' '}
            <span className="md-num" style={{ color: 'var(--acc)', fontWeight: 'var(--fw-semibold)' }}>
              {match.score ?? ''}
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

function KioskSide({ name }: { name: string }) {
  return (
    <div
      style={{
        marginTop: 'var(--sp-4)',
        fontSize: 14,
        fontWeight: 'var(--fw-semibold)',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        color: 'var(--fg-on-dark)',
      }}
    >
      {name}
    </div>
  )
}
