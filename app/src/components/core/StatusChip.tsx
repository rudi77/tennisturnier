import type { CSSProperties } from 'react'
import type { StatusTone } from '../../lib/labels'

const TONE: Record<StatusTone, CSSProperties> = {
  running: { background: 'var(--status-running-bg)', color: 'var(--status-running-fg)' },
  called: { background: 'var(--status-called-bg)', color: 'var(--status-called-fg)' },
  planned: { background: 'var(--status-planned-bg)', color: 'var(--status-planned-fg)' },
  finished: { background: 'var(--status-finished-bg)', color: 'var(--status-finished-fg)' },
  suspended: { background: 'var(--status-suspended-bg)', color: 'var(--status-suspended-fg)' },
}

/**
 * Der Zustand einer Zuweisung als Chip.
 *
 * Fläche und Schrift kommen immer als Paar aus den Tokens — eine Lime-Fläche mit
 * heller Schrift wäre auf dem Aushang unlesbar.
 */
export function StatusChip({
  tone,
  children,
  style,
}: {
  tone: StatusTone
  children: string
  style?: CSSProperties
}) {
  return (
    <span className="md-chip" style={{ ...TONE[tone], ...style }}>
      {children}
    </span>
  )
}
