import { useState } from 'react'
import { AssignmentStatus, MatchStatus, type CourtBoard, type QueuedMatch } from '../../api/types'
import { assignmentStatusLabel, assignmentTone } from '../../lib/labels'
import { formatClock, timeSpanToMinutes } from '../../lib/time'
import { StatusChip } from '../core/StatusChip'
import { TimeLabel } from './TimeLabel'

export type QueueAction = 'call' | 'start' | 'finish' | 'suspend' | 'resume' | 'result'

/**
 * Der Turniertag.
 *
 * Kein Zeitraster mehr: die **Reihenfolge** auf dem Platz ist die Aussage, nicht
 * die Uhrzeit. „Sie sind der Dritte auf Platz 2" ist eine Auskunft, eine
 * Startzeit wäre eine Behauptung, die beim ersten langen Match zerfällt.
 */
export function QueueBoard({
  boards,
  suspended,
  timeZone,
  busyAssignmentId,
  onAction,
  onDropMatch,
  readOnly = false,
}: {
  boards: CourtBoard[]
  /**
   * Die unterbrochenen Partien — sie hängen zwischen den Plätzen.
   *
   * Nicht in einer Spalte, weil sie keinen Platz belegen: unterbrochen heißt,
   * der Platz ist frei. Sie stünden sonst nirgends, und der Weg zurück wäre
   * mit ihnen weg (ADR-0002).
   */
  suspended: QueuedMatch[]
  timeZone: string
  busyAssignmentId: string | null
  onAction: (action: QueueAction, entry: QueuedMatch) => void
  onDropMatch: (matchId: string, courtId: string) => void
  /**
   * Zusehen statt bedienen: keine Knöpfe an den Karten, kein Ziehen. Für
   * jemanden, der zum Turnier gehört, aber es nicht führt — die Reihenfolge
   * am Platz ist für ihn eine Auskunft (ADR-0012).
   */
  readOnly?: boolean
}) {
  const [dragMatchId, setDragMatchId] = useState<string | null>(null)

  return (
    <div className="md-queue">
      {suspended.length > 0 && (
        <div className="md-queue__suspended">
          <div className="md-eyebrow">Unterbrochen</div>
          <div className="md-hint" style={{ fontSize: 'var(--fs-xs)', marginBottom: 'var(--sp-5)' }}>
            Der Platz ist frei. Fortgesetzt wird auf dem Platz, der dann frei ist.
          </div>

          <div style={{ display: 'flex', gap: 'var(--sp-4)', flexWrap: 'wrap' }}>
            {suspended.map((entry) => (
              <QueueCard
                key={entry.assignmentId}
                entry={entry}
                position={0}
                timeZone={timeZone}
                busy={busyAssignmentId === entry.assignmentId}
                onAction={onAction}
                readOnly={readOnly}
              />
            ))}
          </div>
        </div>
      )}

      <div className="md-queue__row">
        {boards.map((board) => {
          // Einmal festgehalten: in einer Rückrufaktion ist `board.current`
          // für den Compiler wieder „vielleicht nichts", und die Absicherung
          // dagegen konnte nie ausschlagen.
          const current = board.current
          const head = current ?? board.queue[0] ?? null
          const live = current?.status === AssignmentStatus.Running

          return (
            <div
              key={board.courtId}
              className={`md-queue__col${live ? ' md-queue__col--live' : ''}`}
              onDragOver={(event) => !readOnly && event.preventDefault()}
              onDrop={() => {
                if (dragMatchId && !readOnly) onDropMatch(dragMatchId, board.courtId)
                setDragMatchId(null)
              }}
            >
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 'var(--sp-3)',
                  padding: 'var(--sp-5) var(--sp-6)',
                  borderBottom: 'var(--border)',
                }}
              >
                <div style={{ fontSize: 12.5, fontWeight: 'var(--fw-bold)', whiteSpace: 'nowrap' }}>
                  {board.courtName}
                </div>
                {board.isCenterCourt && (
                  <div
                    style={{
                      fontSize: 9,
                      fontWeight: 'var(--fw-bold)',
                      letterSpacing: 'var(--ls-wide)',
                      color: 'var(--fg-3)',
                    }}
                  >
                    CENTER
                  </div>
                )}
                <StatusChip
                  tone={head ? assignmentTone(head.status) : 'finished'}
                  style={{ marginLeft: 'auto' }}
                >
                  {head ? assignmentStatusLabel[head.status] : 'frei'}
                </StatusChip>
              </div>

              <div
                style={{
                  padding: 'var(--sp-5)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 'var(--sp-4)',
                  minHeight: 120,
                }}
              >
                {current && (
                  <QueueCard
                    entry={current}
                    position={0}
                    timeZone={timeZone}
                    busy={busyAssignmentId === current.assignmentId}
                    onAction={onAction}
                    onDragStart={() => setDragMatchId(current.matchId)}
                    readOnly={readOnly}
                  />
                )}
                {board.queue.map((entry, index) => (
                  <QueueCard
                    key={entry.assignmentId}
                    entry={entry}
                    position={current ? index + 1 : index}
                    timeZone={timeZone}
                    busy={busyAssignmentId === entry.assignmentId}
                    onAction={onAction}
                    onDragStart={() => setDragMatchId(entry.matchId)}
                    readOnly={readOnly}
                  />
                ))}
                {!current && board.queue.length === 0 && (
                  <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', padding: 'var(--sp-4)' }}>
                    Keine Zuweisung. Karten lassen sich hierher ziehen.
                  </div>
                )}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

function QueueCard({
  entry,
  position,
  timeZone,
  busy,
  onAction,
  onDragStart,
  readOnly,
}: {
  entry: QueuedMatch
  position: number
  timeZone: string
  busy: boolean
  onAction: (action: QueueAction, entry: QueuedMatch) => void
  /**
   * Fehlt, wo die Karte auf keinem Platz liegt.
   *
   * Eine unterbrochene Partie lässt sich nicht umhängen: sie steht auf keinem
   * Platz, und wohin sie käme, entscheidet sich beim Fortsetzen. Ohne Handler
   * ist sie nicht ziehbar — das ist ehrlicher als ein Ziehen, das nichts tut.
   */
  onDragStart?: () => void
  readOnly: boolean
}) {
  const running = entry.status === AssignmentStatus.Running
  const called = entry.status === AssignmentStatus.Called
  const suspended = entry.status === AssignmentStatus.Suspended
  const planned = entry.status === AssignmentStatus.Planned

  // Eingeplant ist der ganze Baum, lange bevor die Teilnehmer feststehen — am
  // Platz wird aber kein Platzhalter ausgerufen.
  const callable = planned && position === 0 && entry.matchStatus === MatchStatus.Ready

  const duration = timeSpanToMinutes(entry.estimatedDuration)
  const className = `md-queue__card${
    running ? ' md-queue__card--running' : called ? ' md-queue__card--called' : ''
  }`

  return (
    <div
      className={className}
      draggable={!readOnly && onDragStart !== undefined}
      onDragStart={onDragStart}
      style={{
        boxShadow: position === 0 ? 'var(--shadow-sm)' : 'none',
        opacity: position > 2 ? 0.72 : 1,
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 6 }}>
        <span className="md-num" style={{ fontSize: 9.5, letterSpacing: '0.04em', opacity: 0.65 }}>
          {entry.label ?? `#${entry.sequenceOnCourt + 1}`}
        </span>
        {running ? (
          <span className="md-time md-time--on-ball">
            seit {formatClock(entry.actualStart, timeZone)}
          </span>
        ) : (
          <TimeLabel
            earliestStart={entry.earliestStart}
            plannedStart={entry.estimatedStart}
            timeZone={timeZone}
            withinOpeningHours={entry.withinOpeningHours}
          />
        )}
      </div>

      <div style={{ fontSize: 'var(--fs-sm)', fontWeight: 'var(--fw-semibold)', marginTop: 6, lineHeight: 1.4 }}>
        {entry.side1 ?? '—'}
      </div>
      <div style={{ fontSize: 'var(--fs-sm)', fontWeight: 'var(--fw-semibold)', lineHeight: 1.4 }}>
        {entry.side2 ?? '—'}
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--sp-3)',
          marginTop: 'var(--sp-4)',
          flexWrap: 'wrap',
        }}
      >
        <span className="md-num" style={{ fontSize: 'var(--fs-xs)', fontWeight: 'var(--fw-semibold)' }}>
          ≈ {duration} min
        </span>
        <StatusChip tone={assignmentTone(entry.status)}>
          {assignmentStatusLabel[entry.status]}
        </StatusChip>
      </div>

      {!entry.withinOpeningHours && (
        <div style={{ fontSize: 10, color: 'var(--danger)', marginTop: 5, lineHeight: 1.35 }}>
          Schätzung liegt außerhalb der Öffnungszeiten — umverteilen oder vertagen.
        </div>
      )}

      {planned && position === 0 && entry.matchStatus !== MatchStatus.Ready && (
        <div style={{ fontSize: 10, color: 'var(--fg-3)', marginTop: 5, lineHeight: 1.35 }}>
          Teilnehmer stehen noch nicht fest — nicht aufrufbar.
        </div>
      )}

      <div style={{ display: 'flex', gap: 'var(--sp-3)', marginTop: 9, flexWrap: 'wrap' }}>
        {!readOnly && callable && (
          <Action label="Aufrufen" variant="md-btn--call" busy={busy} onClick={() => onAction('call', entry)} />
        )}
        {!readOnly && called && (
          <Action label="Start" variant="md-btn--primary" busy={busy} onClick={() => onAction('start', entry)} />
        )}
        {!readOnly && running && (
          <>
            <Action label="Ergebnis" variant="md-btn--primary" busy={busy} onClick={() => onAction('result', entry)} />
            <Action label="Platz frei" busy={busy} onClick={() => onAction('finish', entry)} />
            <Action label="Pause" busy={busy} onClick={() => onAction('suspend', entry)} />
          </>
        )}
        {!readOnly && suspended && (
          <Action label="Fortsetzen" variant="md-btn--primary" busy={busy} onClick={() => onAction('resume', entry)} />
        )}
      </div>
    </div>
  )
}

function Action({
  label,
  variant,
  busy,
  onClick,
}: {
  label: string
  variant?: string
  busy: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      className={`md-btn md-btn--court${variant ? ` ${variant}` : ''}`}
      disabled={busy}
      onClick={onClick}
      style={{ fontSize: 11.5, padding: '0 12px' }}
    >
      {label}
    </button>
  )
}
