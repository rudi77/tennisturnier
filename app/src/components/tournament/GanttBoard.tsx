import { useMemo, useState } from 'react'
import {
  AssignmentStatus,
  type CourtDetail,
  type MatchDetail,
} from '../../api/types'
import { courtMeta, sideName } from '../../lib/labels'
import { dateKey, formatClock, minutesOfDay, timeSpanToMinutes } from '../../lib/time'
import { TimeLabel } from './TimeLabel'
import { useCourtMove, type CourtMove } from './useCourtMove'

/** Das Raster, solange nichts anderes bekannt ist: 09:00–21:00. */
const DEFAULT_START_HOUR = 9
const DEFAULT_END_HOUR = 21
const PX_PER_HOUR = 90

/** Unter zwei Stunden ist das Raster keine Zeitachse mehr, sondern ein Streifen. */
const MIN_HOURS = 2

/** Voreinstellung für eine Dauer, die niemand geschätzt hat. */
const DEFAULT_DURATION_MINUTES = 75

export interface ScheduledMatch {
  match: MatchDetail
  /** Nicht null — nur zugewiesene Matches kommen hierher. */
  courtId: string
}

/** Der gezeigte Ausschnitt des Tages, in ganzen Stunden. */
interface DayRange {
  startHour: number
  endHour: number
}

function topFor(minutes: number, range: DayRange): number {
  return ((minutes - range.startHour * 60) / 60) * PX_PER_HOUR
}

function laneHeight(range: DayRange): number {
  return (range.endHour - range.startHour) * PX_PER_HOUR
}

/** Anfang und Ende einer Karte in Minuten seit Mitternacht, oder null ohne Uhrzeit. */
function span(entry: ScheduledMatch, timeZone: string): { from: number; to: number } | null {
  const assignment = entry.match.assignment
  if (!assignment) return null

  const from = minutesOfDay(
    assignment.actualStart ?? assignment.plannedStart ?? assignment.earliestStart,
    timeZone,
  )
  if (from === null) return null

  return {
    from,
    to: from + (timeSpanToMinutes(assignment.estimatedDuration) || DEFAULT_DURATION_MINUTES),
  }
}

/**
 * Welchen Ausschnitt des Tages das Raster zeigt.
 *
 * Abgeleitet und nicht festgelegt. Hier standen 09:00–21:00 als Konstanten, und
 * ein Turnier, dessen Plätze ab 08:00 gebucht sind, zeichnete seine ersten
 * Matches mit negativem Abstand nach oben: sie lagen über der Platzzeile,
 * verdeckten die Platznamen und wurden am oberen Rand abgeschnitten. Es war
 * nichts verloren — es war nur nicht zu sehen, und zwar genau das, was zuerst
 * gespielt wird.
 *
 * Maßgeblich sind die gebuchten Platzzeiten *und* die Karten. Die Karten
 * deshalb, weil eine Ansetzung von Hand ausdrücklich außerhalb der Platzzeiten
 * liegen darf — sie fällt dann als Verstoß auf und muss dafür sichtbar sein.
 */
function dayRange(
  courts: CourtDetail[],
  scheduled: ScheduledMatch[],
  day: string,
  timeZone: string,
): DayRange {
  const marks: number[] = []

  for (const court of courts) {
    for (const window of court.windows) {
      // Der Anfang trägt beide Prüfungen: unlesbar ist er genau dann, wenn auch
      // sein Tag unlesbar ist. Danach steht er fest, und die frühere
      // Absicherung gegen `null` konnte nie ausschlagen.
      const from = minutesOfDay(window.from, timeZone)
      if (from === null || dateKey(window.from, timeZone) !== day) continue
      marks.push(from)

      // Das Ende kann für sich unlesbar sein; und ein Fenster bis Mitternacht
      // endet als 00:00 des Folgetags.
      const to = minutesOfDay(window.to, timeZone)
      if (to !== null) marks.push(to === 0 ? 24 * 60 : to)
    }
  }

  for (const entry of scheduled) {
    const range = span(entry, timeZone)
    if (range) marks.push(range.from, range.to)
  }

  if (marks.length === 0) return { startHour: DEFAULT_START_HOUR, endHour: DEFAULT_END_HOUR }

  const startHour = Math.min(24 - MIN_HOURS, Math.max(0, Math.floor(Math.min(...marks) / 60)))
  const endHour = Math.min(24, Math.max(Math.ceil(Math.max(...marks) / 60), startHour + MIN_HOURS))

  return { startHour, endHour }
}

/**
 * Der Spielplan im Planungsmodus.
 *
 * Alles, was hier absolut positioniert wird — Karten, geschlossene Zeiten, die Stundenspalte
 * und die Hintergrundstreifen —, leitet sich aus derselben Zahl und demselben
 * `DayRange` ab. Driften sie auseinander, zeichnet das Raster Verfügbarkeit zur
 * falschen Tageszeit.
 *
 * Die Uhrzeit auf einer Karte ist eine Schätzung, solange keine Zusage
 * hinterlegt ist. Deshalb steht dort `TimeLabel` und keine formatierte Zahl.
 */
export function GanttBoard({
  courts,
  scheduled,
  day,
  timeZone,
  onOpenResult,
  onDropMatch,
  readOnly = false,
}: {
  courts: CourtDetail[]
  scheduled: ScheduledMatch[]
  /** "yyyy-MM-dd" — nur Platzzeiten und Karten dieses Tages werden gezeichnet. */
  day: string
  timeZone: string
  onOpenResult: (match: MatchDetail) => void
  onDropMatch: (matchId: string, courtId: string) => void
  /**
   * Zusehen statt planen: kein Ziehen, kein Öffnen einer Ergebnismaske. Für
   * jemanden, der zum Turnier gehört, aber es nicht führt (ADR-0012).
   */
  readOnly?: boolean
}) {
  const verschieben = useCourtMove(onDropMatch, readOnly)
  const [overCourt, setOverCourt] = useState<string | null>(null)

  const range = useMemo(
    () => dayRange(courts, scheduled, day, timeZone),
    [courts, scheduled, day, timeZone],
  )

  const hours = Array.from(
    { length: range.endHour - range.startHour },
    (_, index) => range.startHour + index,
  )

  return (
    <div style={{ overflowX: 'auto', paddingBottom: 'var(--sp-4)' }}>
      <div
        style={{
          display: 'flex',
          minWidth: 1180,
          background: 'var(--surface)',
          border: 'var(--border)',
          borderRadius: 'var(--radius-lg)',
          overflow: 'hidden',
        }}
      >
        <div
          style={{
            width: 56,
            flex: 'none',
            borderRight: 'var(--border)',
            background: 'var(--surface-muted)',
          }}
        >
          <div style={{ height: 52, borderBottom: 'var(--border)' }} />
          {hours.map((hour) => (
            <div
              key={hour}
              className="md-num"
              style={{
                height: PX_PER_HOUR,
                borderBottom: '1px solid var(--line-soft)',
                fontSize: 10.5,
                color: 'var(--fg-3)',
                padding: '4px 0 0 8px',
              }}
            >
              {String(hour).padStart(2, '0')}:00
            </div>
          ))}
        </div>

        {courts.map((court) => {
          const cards = scheduled.filter((entry) => entry.courtId === court.id)
          const closed = closedRanges(court, day, timeZone, range)

          return (
            <div
              key={court.id}
              className={overCourt === court.id ? 'md-gantt__col--drop' : undefined}
              style={{
                flex: '1 0 var(--gantt-col-width)',
                minWidth: 'var(--gantt-col-width)',
                borderRight: 'var(--border)',
                position: 'relative',
              }}
              onDragOver={(event) => {
                if (readOnly) return
                event.preventDefault()
                setOverCourt(court.id)
              }}
              onDragLeave={() => setOverCourt((current) => (current === court.id ? null : current))}
              onDrop={() => {
                setOverCourt(null)
                // Auch hier und nicht nur an `onDragOver`: ohne `preventDefault`
                // nimmt zwar kein Browser den Wurf an, aber eine Zusage, die an
                // einer ausgelassenen Zeile hängt, ist keine.
                verschieben.drop(court.id)
              }}
            >
              <div
                style={{
                  height: 52,
                  borderBottom: 'var(--border)',
                  padding: 'var(--sp-4) var(--sp-5)',
                  background: 'var(--surface-raised)',
                }}
              >
                <div
                  style={{
                    fontSize: 'var(--fs-sm)',
                    fontWeight: 'var(--fw-bold)',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 'var(--sp-3)',
                  }}
                >
                  <span style={{ whiteSpace: 'nowrap' }}>{court.name}</span>
                  {court.isCenterCourt && (
                    <span
                      style={{
                        fontSize: 9,
                        fontWeight: 'var(--fw-semibold)',
                        letterSpacing: 'var(--ls-wide)',
                        color: 'var(--fg-3)',
                      }}
                    >
                      CENTER
                    </span>
                  )}
                </div>
                <div style={{ fontSize: 10, color: 'var(--fg-3)', marginTop: 2 }}>
                  {courtMeta(court.surface, court.location)}
                </div>
              </div>

              {verschieben.picked !== null && (
                <button
                  type="button"
                  className="md-btn md-btn--court md-btn--info"
                  style={{ width: '100%' }}
                  onClick={() => verschieben.drop(court.id)}
                >
                  Auf {court.name}
                </button>
              )}

              <div className="md-gantt__lane" style={{ height: laneHeight(range) }}>
                {closed.map((gap) => (
                  <Closed key={`${gap.from}-${gap.to}`} from={gap.from} to={gap.to} range={range} />
                ))}
                {cards.map((entry) => (
                  <Card
                    key={entry.match.id}
                    entry={entry}
                    timeZone={timeZone}
                    range={range}
                    move={verschieben}
                    onOpen={() => onOpenResult(entry.match)}
                    readOnly={readOnly}
                  />
                ))}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

/**
 * Die Zeit, in der der Platz dem Turnier *nicht* gehört.
 *
 * Hier standen einmal Sperren — Training, Punktespiel, Wartung. Das waren
 * Vereinskalenderdaten, und die sind mit dem Verein entfallen. Im Turnier legt
 * man ein Fenster schlicht nicht an, und was übrig bleibt, ist die Lücke: alles
 * außerhalb der gebuchten Fenster. Sie bleibt sichtbar, denn ein Match, das
 * dort landet, ist kein Fehler des Solvers, sondern eine Ansetzung von Hand,
 * für die niemand einen Platz hat.
 */
function closedRanges(
  court: CourtDetail,
  day: string,
  timeZone: string,
  range: DayRange,
): { from: number; to: number }[] {
  const open = court.windows
    .map((window) => ({
      from: dateKey(window.from, timeZone) === day ? minutesOfDay(window.from, timeZone) : null,
      to: dateKey(window.from, timeZone) === day ? minutesOfDay(window.to, timeZone) : null,
    }))
    .filter((entry): entry is { from: number; to: number } => entry.from !== null && entry.to !== null)
    .sort((a, b) => a.from - b.from)

  const dayStart = range.startHour * 60
  const dayEnd = range.endHour * 60

  // Kein Fenster an diesem Tag heißt: der ganze Tag ist zu. Das ist der
  // Normalfall eines Turniers, dessen Plätze noch nicht gebucht sind — und es
  // soll auffallen, statt als leerer Platz nach Freiraum auszusehen.
  if (open.length === 0) return [{ from: dayStart, to: dayEnd }]

  const gaps: { from: number; to: number }[] = []
  let cursor = dayStart

  for (const range of open) {
    if (range.from > cursor) gaps.push({ from: cursor, to: Math.min(range.from, dayEnd) })
    cursor = Math.max(cursor, range.to)
  }

  if (cursor < dayEnd) gaps.push({ from: cursor, to: dayEnd })

  return gaps.filter((gap) => gap.to > gap.from)
}

function Closed({ from, to, range }: { from: number; to: number; range: DayRange }) {
  return (
    <div
      className="md-gantt__block"
      style={{ top: topFor(from, range), height: ((to - from) / 60) * PX_PER_HOUR }}
      title="Der Platz steht dem Turnier zu dieser Zeit nicht zur Verfügung."
    >
      keine Platzzeit
    </div>
  )
}

function Card({
  entry,
  timeZone,
  range,
  move,
  onOpen,
  readOnly,
}: {
  entry: ScheduledMatch
  timeZone: string
  range: DayRange
  move: CourtMove
  onOpen: () => void
  readOnly: boolean
}) {
  const { match } = entry
  const assignment = match.assignment
  if (!assignment) return null

  const running = assignment.status === AssignmentStatus.Running
  const called = assignment.status === AssignmentStatus.Called

  const minutes = span(entry, timeZone)
  if (!minutes) return null

  const durationMinutes = minutes.to - minutes.from

  // Auf das Raster begrenzt: eine Karte, die über Mitternacht hinausliefe,
  // stünde sonst unter der letzten Stundenzeile und würde vom Rahmen
  // abgeschnitten. Sie ist dann kürzer gezeichnet als geschätzt — sichtbar
  // falsch ist besser als unsichtbar richtig.
  const top = topFor(minutes.from, range)
  const height = Math.min(
    Math.max(74, (durationMinutes / 60) * PX_PER_HOUR - 5),
    laneHeight(range) - top,
  )

  const className = `md-gantt__card${running ? ' md-gantt__card--running' : called ? ' md-gantt__card--called' : ''}`

  return (
    <div
      className={className}
      draggable={!readOnly}
      onDragStart={move.dragStart(match.id)}
      onClick={() => !readOnly && onOpen()}
      role={readOnly ? undefined : 'button'}
      tabIndex={readOnly ? undefined : 0}
      onKeyDown={(event) => {
        if (!readOnly && (event.key === 'Enter' || event.key === ' ')) {
          event.preventDefault()
          onOpen()
        }
      }}
      style={{
        top,
        height,
        outline: move.picked === match.id ? '2px solid var(--blue-400)' : undefined,
      }}
      title={`${sideName(match.side1.participantName, match.side1.origin)} vs ${sideName(
        match.side2.participantName,
        match.side2.origin,
      )}`}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 6, alignItems: 'baseline' }}>
        <span
          className="md-num"
          style={{ fontSize: 9.5, letterSpacing: '0.04em', opacity: 0.6, overflow: 'hidden' }}
        >
          {match.label ?? `#${match.position + 1}`}
        </span>
        {!readOnly && (
          <button
            type="button"
            className="md-gantt__move"
            aria-label={
              move.picked === match.id
                ? 'Verschieben abbrechen'
                : `${match.label ?? `#${match.position + 1}`} auf einen anderen Platz legen`
            }
            aria-pressed={move.picked === match.id}
            onClick={(event) => {
              // Sonst öffnete derselbe Klick die Ergebnismaske: die Karte
              // selbst ist ein Knopf.
              event.stopPropagation()
              move.pick(match.id)
            }}
          >
            {move.picked === match.id ? '×' : '⇄'}
          </button>
        )}
        {running ? (
          <span className="md-time md-time--on-ball">{formatClock(assignment.actualStart, timeZone)}</span>
        ) : (
          <TimeLabel
            earliestStart={assignment.earliestStart}
            plannedStart={assignment.plannedStart}
            timeZone={timeZone}
          />
        )}
      </div>

      <div
        style={{
          fontSize: 11.5,
          fontWeight: 'var(--fw-semibold)',
          marginTop: 5,
          lineHeight: 1.35,
          overflow: 'hidden',
        }}
      >
        {sideName(match.side1.participantName, match.side1.origin)}
      </div>
      <div
        style={{
          fontSize: 11.5,
          fontWeight: 'var(--fw-semibold)',
          lineHeight: 1.35,
          overflow: 'hidden',
        }}
      >
        {sideName(match.side2.participantName, match.side2.origin)}
      </div>

      <div
        className="md-num"
        style={{
          fontSize: 10,
          marginTop: 4,
          opacity: 0.7,
          whiteSpace: 'nowrap',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        {match.score?.display ?? `≈ ${durationMinutes} min${assignment.earliestStart ? ' · Zusage' : ''}`}
      </div>
    </div>
  )
}
