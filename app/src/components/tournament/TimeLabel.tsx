import type { CSSProperties } from 'react'
import { formatClock } from '../../lib/time'

/**
 * Eine Zeit ist entweder eine Zusage oder eine Schätzung.
 *
 * Das ist die tragende Regel des Systems, und sie ist typografisch gelöst,
 * damit sie auch auf einem ausgedruckten Aushang überlebt:
 *
 *   EarliestStart → **14:00** aufrecht, fett, dunkel. Eine Zusage („nicht
 *     vor"), auf die sich ein Spieler verlassen kann. Sie wird nie unterlaufen,
 *     auch wenn der Platz früher frei wird.
 *   PlannedStart  → *~14:25* kursiv, grau, mit Tilde. Eine Schätzung, die sich
 *     mit dem Spielverlauf verschiebt.
 *
 * Eine Schätzung als exakte Uhrzeit zu drucken ist eine Lüge, für die das
 * Werkzeug später verantwortlich gemacht wird. Deshalb gibt es hier keinen
 * Schalter, der das abstellt.
 */
export function TimeLabel({
  earliestStart,
  plannedStart,
  timeZone,
  onBall = false,
  withinOpeningHours = true,
  style,
}: {
  earliestStart: string | null | undefined
  plannedStart: string | null | undefined
  timeZone: string
  /** Auf der Lime-Fläche eines laufenden Matches. */
  onBall?: boolean
  /** false = die Schätzung passt nicht mehr in die Öffnungszeiten des Platzes. */
  withinOpeningHours?: boolean
  style?: CSSProperties
}) {
  if (earliestStart) {
    return (
      <span
        className={`md-time ${onBall ? 'md-time--on-ball' : 'md-time--promise'}`}
        style={style}
        title="Zusage — nicht vor dieser Zeit"
      >
        {formatClock(earliestStart, timeZone)}
      </span>
    )
  }

  if (plannedStart) {
    const overrun = !withinOpeningHours
    return (
      <span
        className={`md-time ${
          onBall ? 'md-time--on-ball' : overrun ? 'md-time--overrun' : 'md-time--estimate'
        }`}
        style={style}
        title={
          overrun
            ? 'Schätzung — liegt außerhalb der Öffnungszeiten des Platzes'
            : 'Schätzung — verschiebt sich mit dem Spielverlauf'
        }
      >
        ~{formatClock(plannedStart, timeZone)}
      </span>
    )
  }

  return (
    <span className="md-time md-time--estimate" style={style}>
      —
    </span>
  )
}

/** Die Legende, die die beiden Formen einmal pro Screen erklärt. */
export function TimeLegend() {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--sp-8)',
        flexWrap: 'wrap',
        fontSize: 'var(--fs-xs)',
        color: 'var(--fg-2)',
        background: 'var(--surface)',
        border: 'var(--border)',
        borderRadius: 'var(--radius)',
        padding: 'var(--sp-4) var(--sp-6)',
      }}
    >
      <span style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-3)' }}>
        <b className="md-time md-time--promise" style={{ fontSize: 'var(--fs-sm)' }}>
          14:00
        </b>
        Zusage · nicht vor
      </span>
      <span style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-3)' }}>
        <i className="md-time md-time--estimate" style={{ fontSize: 'var(--fs-sm)' }}>
          ~14:25
        </i>
        Schätzung
      </span>
      <LegendSwatch color="var(--acc)" label="läuft" />
      <LegendSwatch color="var(--call-400)" label="aufgerufen" />
      <LegendSwatch color="var(--line)" label="Sperre" />
    </div>
  )
}

function LegendSwatch({ color, label }: { color: string; label: string }) {
  return (
    <span style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-3)' }}>
      <span
        style={{
          width: 9,
          height: 9,
          borderRadius: 'var(--radius-xs)',
          background: color,
          display: 'inline-block',
        }}
      />
      {label}
    </span>
  )
}
