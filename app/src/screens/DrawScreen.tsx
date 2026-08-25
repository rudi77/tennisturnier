import { useMemo, useState, type CSSProperties } from 'react'
import { ScreenHeader } from '../components/layout/ScreenHeader'
import { Empty, ErrorBlock, Loading } from '../components/layout/StateBlock'
import { BracketMatch } from '../components/tournament/BracketMatch'
import { DrawPreparation } from '../components/tournament/DrawPreparation'
import { ResultEditor } from '../components/tournament/ResultEditor'
import { useResource } from '../hooks/useResource'
import { useWorkspace } from '../state/WorkspaceContext'
import { bracket as bracketApi } from '../api/endpoints'
import { MatchStatus, TournamentState, type MatchDetail, type PhaseDetail } from '../api/types'
import { timeSpanToMinutes } from '../lib/time'
import { matchFormatOf } from '../lib/matchFormat'
import { isNarrow } from '../lib/breakpoints'

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
 * Der Name einer Runde.
 *
 * Er kommt aus den Etiketten der Matches, nicht aus ihrer Anzahl. Die Anzahl
 * war die naheliegende Regel und ist falsch, sobald eine Runde gemischt ist:
 * Finale und Spiel um Platz 3 liegen in derselben Runde, womit die Zählung zwei
 * ergibt und „Halbfinale" behauptet — für die letzte Runde eines Turniers.
 *
 * Kommen in einer Runde mehrere Etiketten vor, werden sie genannt; ohne
 * Etiketten — etwa in einer Gruppenphase — bleibt die Nummer.
 */
function roundLabel(matches: MatchDetail[], index: number): string {
  const labels = [...new Set(matches.map((match) => match.label).filter(Boolean))] as string[]

  return labels.length > 0 ? labels.join(' · ') : `Runde ${index + 1}`
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
      label: roundLabel(matches, index),
      matches: matches.sort((a, b) => a.position - b.position),
    }))
}

export function DrawScreen() {
  const { tournament, timeZone, reloadTournament } = useWorkspace()
  // Auf einem schmalen Schirm ist der Baum unbenutzbar — die Rundenliste ist
  // die einzige Darstellung, die ohne Zoom trägt, und das steht seit jeher in
  // ihrem eigenen Hinweistext. Einmal beim Aufbau gelesen und nicht bei jeder
  // Größenänderung: wer danach umschaltet, hat sich entschieden, und ein
  // Drehen des Geräts soll ihn nicht zurücksetzen.
  const [style, setStyle] = useState<BracketStyle>(() => (isNarrow() ? 'list' : 'tree'))
  const [phaseId, setPhaseId] = useState<string | null>(null)
  const [editing, setEditing] = useState<MatchDetail | null>(null)

  const tournamentId = tournament?.id ?? null

  const phases = useResource(
    () => bracketApi.phases(tournamentId as string),
    [tournamentId],
    { enabled: !!tournamentId },
  )

  // Einmal entpackt und danach überall dieselbe Liste: `phases.data ?? []`
  // stand fünfmal da, und vier davon konnten den leeren Fall gar nicht
  // erreichen — sie standen hinter der Ladeanzeige.
  const phaseList = phases.data ?? []

  const phase = useMemo(() => {
    const list = phases.data ?? []
    return list.find((entry) => entry.id === phaseId) ?? list[0] ?? null
  }, [phases.data, phaseId])

  /**
   * Das Satzformat des bearbeiteten Matches.
   *
   * Es steht an der Phase, in der das Match liegt — und das ist immer die
   * gezeigte: bearbeitet wird nur, was im Baum steht, und im Baum steht nur
   * diese Phase.
   */
  const editingFormat = useMemo(
    () => matchFormatOf(tournament?.format?.definition, phase?.ordinal ?? null),
    [tournament?.format?.definition, phase],
  )

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
      <section className="md-section">
        <ScreenHeader
          title="Draw & Bracket"
          lead={
            tournament
              ? `${phase ? phase.name : 'noch kein Draw'} · ${tournament.entries.length} Meldungen`
              : 'Kein Turnier ausgewählt'
          }
          stats={kpis}
        />
        {!tournament ? (
          <Empty title="Kein Turnier ausgewählt" />
        ) : beforeDraw ? (
          // Nicht nur der Befund „kein Draw", sondern der Weg dorthin: Meldung
          // öffnen, Teilnehmer melden, Meldeschluss, auslosen. Ohne ihn bleibt
          // ein frisch angelegtes Turnier im Entwurf stehen.
          <DrawPreparation
            tournament={tournament}
            onChanged={async () => {
              // Auch das Bracket neu laden, nicht nur das Turnier: die
              // Auslosung erzeugt die Matches, und ohne diesen Aufruf zeigte der
              // Screen unmittelbar danach „Keine Matches in dieser Phase" — der
              // Draw war da, nur nicht geholt.
              await Promise.all([reloadTournament(), phases.reload()])
            }}
          />
        ) : phases.error ? (
          <ErrorBlock error={phases.error} onRetry={() => void phases.reload()} />
        ) : phases.loading && !phases.data ? (
          <Loading label="Bracket wird geladen …" />
        ) : !phase || rounds.length === 0 ? (
          // Beides führt zum selben leeren Zustand — und die Prüfung auf die
          // Phase steht voran, damit weiter unten feststeht, dass es sie gibt.
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

              {phaseList.length > 1 && (
                <select
                  className="md-input"
                  aria-label="Phase"
                  value={phase.id}
                  onChange={(event) => setPhaseId(event.target.value)}
                >
                  {phaseList.map((entry) => (
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
          format={editingFormat}
          meta={`${editing.assignment?.courtName ?? 'ohne Platz'}${
            editing.assignment ? ` · ≈ ${timeSpanToMinutes(editing.assignment.estimatedDuration)} min` : ''
          } · ${timeZone}`}
          nextRoundName={nextRoundName}
          onClose={() => setEditing(null)}
          // Auch das Turnier neu laden, nicht nur das Bracket: sein Zustand
          // folgt aus den Ergebnissen. Das erste macht aus einem ausgelosten
          // ein laufendes Turnier, das letzte aus einem laufenden ein
          // abgeschlossenes. Ohne diesen Aufruf stand das Finale eingetragen
          // im Baum, während der Ablauf weiter auf „Spielen" zeigte — der
          // Server war weiter, die Oberfläche hielt eine Kopie von vorher.
          onSaved={async () => {
            await Promise.all([phases.reload(), reloadTournament()])
          }}
        />
      )}
    </>
  )
}

/**
 * Wie viele Matches eine Runde als Baumknoten trägt.
 *
 * Die erste so viele, wie sie hat; jede weitere die Hälfte der vorigen. Was
 * darüber hinaus in derselben Runde liegt, hängt nicht am Baum — das Spiel um
 * Platz 3 folgt aus den Halbfinals und nicht aus dem Finale, steht aber in
 * dessen Runde.
 */
function withSlots(rounds: Round[]): { round: Round; inTree: MatchDetail[]; aside: MatchDetail[] }[] {
  let slots = 0

  return rounds.map((round, index) => {
    slots = index === 0 ? round.matches.length : Math.ceil(slots / 2)

    return {
      round,
      inTree: round.matches.filter((match) => match.position <= slots),
      aside: round.matches.filter((match) => match.position > slots),
    }
  })
}

/**
 * Der klassische Baum.
 *
 * Die Teilung verdoppelt sich je Runde, damit ein Match auf der Mitte seiner
 * beiden Vorgänger sitzt. Gerechnet wird in CSS mit `--pitch` und der festen
 * Kartenhöhe: die Zahlen standen einmal hier als 60 und 52, und die 52 stimmte
 * nicht — die Karte war 56 hoch. Vier Pixel je Runde, kumulativ, und im
 * Halbfinale sah man es.
 *
 * Der zweite Grund für den Versatz war die Überschrift: sie stand im selben
 * Behälter wie die Karten und bekam damit deren Abstand als Abstand zu sich
 * selbst — und der wächst je Runde. Sie steht jetzt daneben.
 */
function TreeView({ rounds, onOpen }: { rounds: Round[]; onOpen: (match: MatchDetail) => void }) {
  const columns = useMemo(() => withSlots(rounds), [rounds])

  return (
    <div className="md-panel" style={{ overflow: 'auto', padding: 18 }}>
      <div className="md-bracket__rounds">
        {columns.map(({ round, inTree, aside }, index) => (
          <div
            key={round.index}
            className="md-bracket__round"
            style={{ '--pitch': `calc(var(--bracket-pitch) * ${2 ** index})` } as CSSProperties}
          >
            <div className="md-eyebrow">{roundLabel(inTree, index)}</div>

            <div className="md-bracket__slots">
              {inTree.map((match) => (
                <div key={match.id} className="md-bracket__slot">
                  {index > 0 && (
                    <div
                      className="md-bracket__connector"
                      style={{ height: `calc(var(--pitch) / 2)` }}
                      aria-hidden="true"
                    />
                  )}
                  <BracketMatch match={match} onOpen={onOpen} />
                </div>
              ))}
            </div>

            {aside.length > 0 && (
              <div className="md-bracket__aside">
                <div className="md-eyebrow">{roundLabel(aside, index)}</div>
                {aside.map((match) => (
                  <BracketMatch key={match.id} match={match} onOpen={onOpen} />
                ))}
              </div>
            )}
          </div>
        ))}
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
