import { useState } from 'react'
import {
  AssignmentStatus,
  type BlockDetail,
  type CourtDetail,
  type MatchDetail,
} from '../../api/types'
import { blockReasonLabel, courtMeta, sideName } from '../../lib/labels'
import { dateKey, formatClock, minutesOfDay, timeSpanToMinutes } from '../../lib/time'
import { TimeLabel } from './TimeLabel'

/** Das Raster: 09:00–21:00, wie tokens/scheduling.css es festlegt. */
const DAY_START_HOUR = 9
const DAY_HOURS = 12
const PX_PER_HOUR = 90

export interface ScheduledMatch {
  match: MatchDetail
  /** Nicht null — nur zugewiesene Matches kommen hierher. */
  courtId: string
}

function topFor(minutes: number): number {
  return ((minutes - DAY_START_HOUR * 60) / 60) * PX_PER_HOUR
}

/**
 * Der Spielplan im Planungsmodus.
 *
 * Alles, was hier absolut positioniert wird — Karten, Sperren, die Stundenspalte
 * und die Hintergrundstreifen —, leitet sich aus derselben Zahl ab. Driften sie
 * auseinander, zeichnet das Raster Verfügbarkeit zur falschen Tageszeit.
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
}: {
  courts: CourtDetail[]
  scheduled: ScheduledMatch[]
  /** "yyyy-MM-dd" — nur Sperren und Karten dieses Tages werden gezeichnet. */
  day: string
  timeZone: string
  onOpenResult: (match: MatchDetail) => void
  onDropMatch: (matchId: string, courtId: string) => void
}) {
  const [dragId, setDragId] = useState<string | null>(null)
  const [overCourt, setOverCourt] = useState<string | null>(null)

  const hours = Array.from({ length: DAY_HOURS }, (_, index) => DAY_START_HOUR + index)

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
          const blocks = court.blocks.filter((block) => dateKey(block.from, timeZone) === day)

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
                event.preventDefault()
                setOverCourt(court.id)
              }}
              onDragLeave={() => setOverCourt((current) => (current === court.id ? null : current))}
              onDrop={() => {
                setOverCourt(null)
                if (dragId) onDropMatch(dragId, court.id)
                setDragId(null)
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

              <div className="md-gantt__lane" style={{ height: DAY_HOURS * PX_PER_HOUR }}>
                {blocks.map((block) => (
                  <Block key={block.id} block={block} timeZone={timeZone} />
                ))}
                {cards.map((entry) => (
                  <Card
                    key={entry.match.id}
                    entry={entry}
                    timeZone={timeZone}
                    onDragStart={() => setDragId(entry.match.id)}
                    onOpen={() => onOpenResult(entry.match)}
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

/** Sperren liegen auf derselben Skala wie Karten und Zeitachse — sonst sitzt der
 *  16:00-Block optisch woanders als das Match, das er verdrängt. */
function Block({ block, timeZone }: { block: BlockDetail; timeZone: string }) {
  const from = minutesOfDay(block.from, timeZone)
  const to = minutesOfDay(block.to, timeZone)
  if (from === null || to === null) return null

  return (
    <div
      className="md-gantt__block"
      style={{ top: topFor(from), height: ((to - from) / 60) * PX_PER_HOUR }}
      title={block.note ?? blockReasonLabel[block.reason]}
    >
      {blockReasonLabel[block.reason]}
      {block.note ? ` · ${block.note}` : ''}
    </div>
  )
}

function Card({
  entry,
  timeZone,
  onDragStart,
  onOpen,
}: {
  entry: ScheduledMatch
  timeZone: string
  onDragStart: () => void
  onOpen: () => void
}) {
  const { match } = entry
  const assignment = match.assignment
  if (!assignment) return null

  const running = assignment.status === AssignmentStatus.Running
  const called = assignment.status === AssignmentStatus.Called

  const startIso = assignment.actualStart ?? assignment.plannedStart ?? assignment.earliestStart
  const startMinutes = minutesOfDay(startIso, timeZone)
  if (startMinutes === null) return null

  const durationMinutes = timeSpanToMinutes(assignment.estimatedDuration) || 75
  const height = Math.max(74, (durationMinutes / 60) * PX_PER_HOUR - 5)

  const className = `md-gantt__card${running ? ' md-gantt__card--running' : called ? ' md-gantt__card--called' : ''}`

  return (
    <div
      className={className}
      draggable
      onDragStart={onDragStart}
      onClick={onOpen}
      role="button"
      tabIndex={0}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          onOpen()
        }
      }}
      style={{ top: topFor(startMinutes), height }}
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
