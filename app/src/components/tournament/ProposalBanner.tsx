import { useState } from 'react'
import type { SchedulePlanResult } from '../../api/types'
import { constraintLabel, proposalChangeLabel } from '../../lib/labels'
import { formatClock } from '../../lib/time'

/**
 * Der Vorschlag des Solvers (ADR-0002).
 *
 * Rechnen und Übernehmen sind zwei Schritte. Ein Solverlauf, der den Plan still
 * überschreibt, ist genau das, was Turnierleitungen dazu bringt, die Automatik
 * abzuschalten — deshalb steht hier ein Diff und ein Knopf, und nicht das
 * Ergebnis.
 *
 * Die Begründung je Zuweisung ist kein Beiwerk: wer nicht sieht, warum ein Match
 * dort liegt, wo es liegt, verschiebt es lieber von Hand.
 */
export function ProposalBanner({
  proposal,
  timeZone,
  busy,
  onConfirm,
  onDiscard,
}: {
  proposal: SchedulePlanResult
  timeZone: string
  busy: boolean
  onConfirm: () => void
  onDiscard: () => void
}) {
  const [expanded, setExpanded] = useState(false)
  const { diff } = proposal

  return (
    <div
      style={{
        border: '1px solid var(--blue-400)',
        background: 'var(--surface-info)',
        borderRadius: 'var(--radius-md)',
        padding: 'var(--sp-7) var(--sp-8)',
        marginBottom: 'var(--sp-8)',
      }}
    >
      <div style={{ display: 'flex', gap: 'var(--sp-8)', alignItems: 'flex-start', flexWrap: 'wrap' }}>
        <div style={{ flex: '1 1 260px' }}>
          <div style={{ fontSize: 'var(--fs-md)', fontWeight: 'var(--fw-bold)', marginBottom: 4 }}>
            ScheduleProposal · Diff{' '}
            <span className="md-num">
              {diff.moved} verschoben · {diff.added} neu · {diff.unchanged} unverändert
              {diff.removed > 0 ? ` · ${diff.removed} entfallen` : ''}
            </span>
          </div>
          <div className="md-hint">
            Kritischer Pfad zuerst, danach lokale Suche. Von Hand gesetzte und gepinnte Zuweisungen
            gingen als harte Vorgabe in den Lauf ein. Nichts davon ist eingetragen, solange nicht
            übernommen wird.
          </div>

          {proposal.violations.length > 0 && (
            <div style={{ marginTop: 'var(--sp-5)' }}>
              <div className="md-eyebrow" style={{ color: 'var(--danger)' }}>
                Verstöße
              </div>
              <ul style={{ margin: '4px 0 0', paddingLeft: 18, fontSize: 'var(--fs-xs)', color: 'var(--fg-2)' }}>
                {proposal.violations.map((violation, index) => (
                  <li key={`${violation.assignmentId}-${index}`} style={{ lineHeight: 1.5 }}>
                    <b>{constraintLabel[violation.constraint]}</b> — {violation.message}
                  </li>
                ))}
              </ul>
            </div>
          )}

          {proposal.unscheduled.length > 0 && (
            <div style={{ marginTop: 'var(--sp-5)' }}>
              <div className="md-eyebrow">Ohne Platz geblieben</div>
              <ul style={{ margin: '4px 0 0', paddingLeft: 18, fontSize: 'var(--fs-xs)', color: 'var(--fg-2)' }}>
                {proposal.unscheduled.map((entry) => (
                  <li key={entry.matchId} style={{ lineHeight: 1.5 }}>
                    <span className="md-num">{entry.label ?? entry.matchId.slice(0, 8)}</span> —{' '}
                    {entry.reason}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>

        <div style={{ display: 'flex', gap: 'var(--sp-4)' }}>
          <button
            type="button"
            className="md-btn md-btn--info"
            onClick={onConfirm}
            disabled={busy || proposal.assignments.length === 0}
          >
            {busy ? 'Übernimmt …' : 'Übernehmen'}
          </button>
          <button type="button" className="md-btn" onClick={onDiscard} disabled={busy}>
            Verwerfen
          </button>
        </div>
      </div>

      {proposal.assignments.length > 0 && (
        <div style={{ marginTop: 'var(--sp-6)' }}>
          <button
            type="button"
            onClick={() => setExpanded((value) => !value)}
            style={{
              background: 'none',
              border: 0,
              padding: 0,
              cursor: 'pointer',
              fontFamily: 'inherit',
              fontSize: 'var(--fs-xs)',
              fontWeight: 'var(--fw-semibold)',
              color: 'var(--blue-400)',
            }}
            aria-expanded={expanded}
          >
            {expanded ? 'Begründungen ausblenden' : `Begründungen anzeigen (${proposal.assignments.length})`}
          </button>

          {expanded && (
            <div
              style={{
                marginTop: 'var(--sp-5)',
                maxHeight: 260,
                overflowY: 'auto',
                border: 'var(--border)',
                borderRadius: 'var(--radius)',
                background: 'var(--surface)',
              }}
            >
              {proposal.assignments.map((assignment) => (
                <div
                  key={assignment.matchId}
                  style={{
                    display: 'flex',
                    gap: 'var(--sp-5)',
                    padding: 'var(--sp-4) var(--sp-6)',
                    borderBottom: '1px solid var(--line-hairline)',
                    fontSize: 'var(--fs-xs)',
                    alignItems: 'baseline',
                  }}
                >
                  <span className="md-num" style={{ width: 54, flex: 'none', color: 'var(--fg-3)' }}>
                    {assignment.label ?? assignment.matchId.slice(0, 6)}
                  </span>
                  <span className="md-num" style={{ width: 96, flex: 'none' }}>
                    {assignment.courtName} · {formatClock(assignment.plannedStart, timeZone)}
                  </span>
                  <span
                    className="md-chip"
                    style={{
                      flex: 'none',
                      background: 'var(--blue-100)',
                      color: 'var(--court-700)',
                    }}
                  >
                    {proposalChangeLabel[assignment.change]}
                  </span>
                  <span style={{ flex: 1, color: 'var(--fg-2)', lineHeight: 1.45 }}>
                    {assignment.reason}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
