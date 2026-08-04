import { useMemo, useState } from 'react'
import { PageHeader } from '../components/layout/PageHeader'
import { TournamentPicker } from '../components/layout/TournamentPicker'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { BracketMatch } from '../components/tournament/BracketMatch'
import { DrawPreparation } from '../components/tournament/DrawPreparation'
import { ResultEditor } from '../components/tournament/ResultEditor'
import { useResource } from '../hooks/useResource'
import { useWorkspace } from '../state/WorkspaceContext'
import { bracket as bracketApi } from '../api/endpoints'
import { MatchStatus, TournamentState, type MatchDetail, type PhaseDetail } from '../api/types'
import { timeSpanToMinutes } from '../lib/time'

type BracketStyle = 'tree' | 'cols' | 'list'

const STYLES: { id: BracketStyle; label: string }[] = [
  { id: 'tree', label: 'Baum mit Verbindungen' },
  { id: 'cols', label: 'Kompakte Rundenspalten' },
  { id: 'list', label: 'Rundenliste (mobil)' },
]

const HINTS: Record<BracketStyle, string> = {
  tree: 'Klassisch — gut am Aushang, braucht Breite. Klick auf ein Match öffnet die Ergebniseingabe; der Sieger propagiert automatisch.',
  cols: 'Dichter: gleiche Information ohne Verbindungslinien, passt bei 64 und 128 auf einen Bildschirm.',
  list: 'Rundenweise Liste mit Fortschritt — die einzige Variante, die auf dem Handy ohne Zoom funktioniert.',
}

interface Round {
  index: number
  label: string
  matches: MatchDetail[]
}

/**
 * Rundennamen aus der Bracketgröße.
 *
 * Die API nummeriert Runden nur; „Halbfinale" ist eine Anzeigefrage und hängt
 * daran, wie viele Matches die Runde hat — bei einem 32er-Feld ist Runde 3 das
 * Viertelfinale, bei einem 8er-Feld das Finale.
 */
function roundLabel(matchCount: number, index: number): string {
  switch (matchCount) {
    case 1:
      return 'Finale'
    case 2:
      return 'Halbfinale'
    case 4:
      return 'Viertelfinale'
    case 8:
      return 'Achtelfinale'
    default:
      return `Runde ${index + 1}`
  }
}

function toRounds(phase: PhaseDetail | null): Round[] {
  if (!phase) return []
  const byRound = new Map<number, MatchDetail[]>()
  for (const match of phase.matches) {
    const list = byRound.get(match.round) ?? []
    list.push(match)
    byRound.set(match.round, list)
  }
  return [...byRound.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([round, matches], index) => ({
      index: round,
      label: roundLabel(matches.length, index),
      matches: matches.sort((a, b) => a.position - b.position),
    }))
}

export function DrawScreen() {
  const { tournament, timeZone, reloadTournament } = useWorkspace()
  const [style, setStyle] = useState<BracketStyle>('tree')
  const [phaseId, setPhaseId] = useState<string | null>(null)
  const [editing, setEditing] = useState<MatchDetail | null>(null)

  const tournamentId = tournament?.id ?? null

  const phases = useResource(
    () => bracketApi.phases(tournamentId as string),
    [tournamentId],
    { enabled: !!tournamentId },
  )

  const phase = useMemo(() => {
    const list = phases.data ?? []
    return list.find((entry) => entry.id === phaseId) ?? list[0] ?? null
  }, [phases.data, phaseId])

  const rounds = useMemo(() => toRounds(phase), [phase])

  const kpis = useMemo(() => {
    const all = phase?.matches ?? []
    const finished = all.filter((match) => match.status === MatchStatus.Finished).length
    return [
      { value: all.length, label: 'Matches' },
      { value: finished, label: 'fertig', color: 'var(--fg-3)' },
      { value: all.length - finished, label: 'offen', color: 'var(--court-700)' },
    ]
  }, [phase])

  const nextRoundName = useMemo(() => {
    if (!editing) return null
    const current = rounds.findIndex((round) => round.index === editing.round)
    return rounds[current + 1]?.label ?? null
  }, [editing, rounds])

  const beforeDraw =
    tournament != null &&
    (tournament.state === TournamentState.Draft ||
      tournament.state === TournamentState.RegistrationOpen ||
      tournament.state === TournamentState.RegistrationClosed)

  return (
    <>
      <PageHeader
        title="Draw & Bracket"
        tag={phase ? phase.name : 'kein Draw'}
        subtitle={
          tournament
            ? `${tournament.name} · ${tournament.entries.length} Meldungen`
            : 'Kein Turnier ausgewählt'
        }
        kpis={kpis}
      >
        <TournamentPicker />
      </PageHeader>

      <section className="md-section">
        {!tournament ? (
          <Empty title="Kein Turnier ausgewählt" />
        ) : beforeDraw ? (
          // Nicht nur der Befund „kein Draw", sondern der Weg dorthin: Meldung
          // öffnen, Teilnehmer melden, Meldeschluss, auslosen. Ohne ihn bleibt
          // ein frisch angelegtes Turnier im Entwurf stehen.
          <DrawPreparation tournament={tournament} onChanged={reloadTournament} />
        ) : phases.error ? (
          <ErrorBlock error={phases.error} onRetry={() => void phases.reload()} />
        ) : phases.loading && !phases.data ? (
          <Loading label="Bracket wird geladen …" />
        ) : rounds.length === 0 ? (
          <Empty title="Keine Matches in dieser Phase" />
        ) : (
          <>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 'var(--sp-6)',
                flexWrap: 'wrap',
                marginBottom: 'var(--sp-10)',
              }}
            >
              <div className="md-pillbar">
                {STYLES.map((entry) => (
                  <button
                    key={entry.id}
                    type="button"
                    className="md-seg"
                    aria-pressed={style === entry.id}
                    onClick={() => setStyle(entry.id)}
                  >
                    {entry.label}
                  </button>
                ))}
              </div>

              {(phases.data ?? []).length > 1 && (
                <select
                  className="md-input"
                  aria-label="Phase"
                  value={phase?.id ?? ''}
                  onChange={(event) => setPhaseId(event.target.value)}
                >
                  {(phases.data ?? []).map((entry) => (
                    <option key={entry.id} value={entry.id}>
                      {entry.ordinal}. {entry.name}
                    </option>
                  ))}
                </select>
              )}

              <div className="md-hint" style={{ maxWidth: 560 }}>
                {HINTS[style]}
              </div>
            </div>

            {style === 'tree' && <TreeView rounds={rounds} onOpen={setEditing} />}
            {style === 'cols' && <ColumnsView rounds={rounds} onOpen={setEditing} />}
            {style === 'list' && <ListView rounds={rounds} onOpen={setEditing} />}
          </>
        )}
      </section>

      {editing && (
        <ResultEditor
          match={editing}
          matchLabel={editing.label ?? editing.id.slice(0, 8)}
          meta={`${editing.assignment?.courtName ?? 'ohne Platz'}${
            editing.assignment ? ` · ≈ ${timeSpanToMinutes(editing.assignment.estimatedDuration)} min` : ''
          } · ${timeZone}`}
          nextRoundName={nextRoundName}
          onClose={() => setEditing(null)}
          onSaved={phases.reload}
        />
      )}
    </>
  )
}

/** Der klassische Baum. Der Abstand verdoppelt sich je Runde — sonst treffen
 *  sich die Bügel nicht auf der Mitte des Folgematches. */
function TreeView({ rounds, onOpen }: { rounds: Round[]; onOpen: (match: MatchDetail) => void }) {
  const PITCH = 60

  return (
    <div
      className="md-panel"
      style={{ overflow: 'auto', padding: 18 }}
    >
      <div style={{ display: 'flex', alignItems: 'stretch', minWidth: 1240 }}>
        {rounds.map((round, index) => {
          const pitch = PITCH * Math.pow(2, index)
          return (
            <div
              key={round.index}
              style={{
                display: 'flex',
                flexDirection: 'column',
                gap: pitch - 52,
                paddingTop: pitch / 2 - 26,
                flex: '0 0 auto',
              }}
            >
              <div className="md-eyebrow" style={{ marginBottom: 'var(--sp-5)' }}>
                {round.label}
              </div>
              {round.matches.map((match) => (
                <div key={match.id} style={{ display: 'flex', alignItems: 'center' }}>
                  {index > 0 && (
                    <div
                      className="md-bracket__connector"
                      style={{ height: PITCH * Math.pow(2, index - 1) }}
                      aria-hidden="true"
                    />
                  )}
                  <BracketMatch match={match} onOpen={onOpen} />
                </div>
              ))}
            </div>
          )
        })}
      </div>
    </div>
  )
}

function ColumnsView({ rounds, onOpen }: { rounds: Round[]; onOpen: (match: MatchDetail) => void }) {
  return (
    <div className="md-panel" style={{ overflowX: 'auto', padding: 18 }}>
      <div style={{ display: 'flex', gap: 'var(--sp-7)', minWidth: 1080, alignItems: 'flex-start' }}>
        {rounds.map((round) => {
          const done = round.matches.filter((match) => match.status === MatchStatus.Finished).length
          return (
            <div key={round.index} style={{ flex: 1, minWidth: 190 }}>
              <div
                style={{
                  display: 'flex',
                  alignItems: 'baseline',
                  justifyContent: 'space-between',
                  marginBottom: 9,
                }}
              >
                <div className="md-eyebrow">{round.label}</div>
                <div className="md-num" style={{ fontSize: 10, color: 'var(--fg-3)' }}>
                  {done}/{round.matches.length}
                </div>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
                {round.matches.map((match) => (
                  <BracketMatch key={match.id} match={match} compact onOpen={onOpen} />
                ))}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

function ListView({ rounds, onOpen }: { rounds: Round[]; onOpen: (match: MatchDetail) => void }) {
  return (
    <div style={{ maxWidth: 640, display: 'flex', flexDirection: 'column', gap: 'var(--sp-6)' }}>
      {rounds.map((round) => {
        const done = round.matches.filter((match) => match.status === MatchStatus.Finished).length
        return (
          <div key={round.index} className="md-panel" style={{ overflow: 'hidden' }}>
            <div
              style={{
                padding: 'var(--sp-6) var(--sp-7)',
                display: 'flex',
                alignItems: 'center',
                gap: 'var(--sp-5)',
                borderBottom: '1px solid var(--line-soft)',
              }}
            >
              <div style={{ fontSize: 'var(--fs-md)', fontWeight: 'var(--fw-bold)' }}>{round.label}</div>
              <div className="md-num" style={{ fontSize: 10.5, color: 'var(--fg-3)' }}>
                {done} von {round.matches.length}
              </div>
              <div
                style={{
                  marginLeft: 'auto',
                  width: 110,
                  height: 5,
                  borderRadius: 'var(--radius-pill)',
                  background: 'var(--line-soft)',
                  overflow: 'hidden',
                }}
              >
                <div
                  style={{
                    width: `${(done / round.matches.length) * 100}%`,
                    height: '100%',
                    background: 'var(--acc)',
                  }}
                />
              </div>
            </div>
            <div style={{ padding: 'var(--sp-5)', display: 'flex', flexDirection: 'column', gap: 5 }}>
              {round.matches.map((match) => (
                <BracketMatch key={match.id} match={match} compact onOpen={onOpen} />
              ))}
            </div>
          </div>
        )
      })}
    </div>
  )
}
