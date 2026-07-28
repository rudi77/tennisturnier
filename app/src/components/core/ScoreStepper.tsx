/**
 * Games eines Satzes, mit dem Daumen bedienbar.
 *
 * Die Eingabe steht am Platz auf einem Tablet — deshalb keine Zahlenfelder,
 * sondern Flächen, die man ohne Tastatur trifft.
 */
export function ScoreStepper({
  value,
  onChange,
  label,
  max = 9,
}: {
  value: number
  onChange: (next: number) => void
  label: string
  max?: number
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 2, width: 64, justifyContent: 'center' }}>
      <button
        type="button"
        aria-label={`${label} verringern`}
        onClick={() => onChange(Math.max(0, value - 1))}
        style={{
          width: 22,
          height: 34,
          border: 'var(--border)',
          background: 'var(--surface)',
          borderRadius: 'var(--radius-sm) 0 0 var(--radius-sm)',
          color: 'var(--court-700)',
          fontSize: 14,
          cursor: 'pointer',
          fontFamily: 'inherit',
        }}
      >
        –
      </button>
      <div
        className="md-num"
        aria-label={label}
        style={{
          width: 26,
          height: 34,
          display: 'grid',
          placeItems: 'center',
          borderTop: 'var(--border)',
          borderBottom: 'var(--border)',
          fontSize: 14,
          fontWeight: 'var(--fw-semibold)',
        }}
      >
        {value}
      </div>
      <button
        type="button"
        aria-label={`${label} erhöhen`}
        onClick={() => onChange(Math.min(max, value + 1))}
        style={{
          width: 22,
          height: 34,
          border: 'var(--border)',
          background: 'var(--surface)',
          borderRadius: '0 var(--radius-sm) var(--radius-sm) 0',
          color: 'var(--court-700)',
          fontSize: 14,
          cursor: 'pointer',
          fontFamily: 'inherit',
        }}
      >
        +
      </button>
    </div>
  )
}
