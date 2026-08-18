/**
 * Das Satzformat einstellen.
 *
 * Drei Fragen, und alle drei entscheidet der Nachmittag und nicht der Modus:
 *
 *  - Über wie viele Sätze wird gespielt?
 *  - Bis wie viele Spiele geht ein Satz? Vier statt sechs ist der Unterschied
 *    zwischen einem Turnier, das um sechs fertig ist, und einem, das es nicht
 *    ist.
 *  - Und der entscheidende Satz — voll ausgespielt oder als Champions-Tiebreak
 *    bis 10?
 *
 * Die Regeln stehen in `Domain/Formats/FormatDefinition.cs` (MatchFormat) und
 * werden dort durchgesetzt; hier stehen sie als Auswahl, damit niemand eine
 * Einstellung treffen kann, die der Server abweist.
 */

import { FinalSetMode, type MatchFormat } from '../../api/types'
import { matchFormatSummary } from '../../lib/matchFormat'

/** Die Spielzahl je Satz, die eine Domänenprüfung durchlässt. */
const MIN_GAMES = 1
const MAX_GAMES = 12

const BEST_OF: { value: number; label: string }[] = [
  { value: 1, label: 'ein Satz' },
  { value: 3, label: '2 Gewinnsätze' },
  { value: 5, label: '3 Gewinnsätze' },
]

/**
 * Die üblichen Satzlängen als Abkürzung. Wer eine andere braucht, trägt sie
 * daneben ein — die Domäne lässt 1 bis 12 zu, und eine Auswahl, die weniger
 * anbietet als erlaubt ist, sieht aus wie eine Regel.
 */
const COMMON_GAMES = [4, 6, 8]

const FINAL_SET: { value: FinalSetMode; label: string; hint: string }[] = [
  {
    value: FinalSetMode.MatchTiebreak10,
    label: 'Champions-Tiebreak',
    hint: 'statt des letzten Satzes, bis 10 mit zwei Punkten Vorsprung',
  },
  {
    value: FinalSetMode.Regular,
    label: 'wie jeder Satz',
    hint: 'mit Tiebreak beim Gleichstand',
  },
  {
    value: FinalSetMode.Advantage,
    label: 'Vorteilssatz',
    hint: 'ohne Tiebreak, bis zwei Spiele Vorsprung stehen',
  },
]

export function MatchFormatPicker({
  value,
  onChange,
  disabled = false,
}: {
  value: MatchFormat
  onChange: (next: MatchFormat) => void
  disabled?: boolean
}) {
  const patch = (part: Partial<MatchFormat>) => onChange({ ...value, ...part })

  // Bei einem einzigen Satz gibt es keinen „letzten" neben anderen — die Wahl
  // bleibt trotzdem stehen, denn sie entscheidet dann, ob überhaupt ein Satz
  // oder nur ein Champions-Tiebreak gespielt wird.
  const decidingHint =
    value.bestOf === 1
      ? 'Bei einem einzigen Satz ist er das ganze Match.'
      : `Nur der ${value.bestOf}. Satz — die davor werden normal gespielt.`

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--sp-7)' }}>
      <Row label="Sätze" hint="wie viele zum Sieg nötig sind">
        {BEST_OF.map((option) => (
          <button
            key={option.value}
            type="button"
            className="md-pill"
            aria-pressed={value.bestOf === option.value}
            disabled={disabled}
            onClick={() => patch({ bestOf: option.value })}
          >
            {option.label}
          </button>
        ))}
      </Row>

      <Row label="Spiele pro Satz" hint="kürzere Sätze, kürzeres Turnier">
        {COMMON_GAMES.map((games) => (
          <button
            key={games}
            type="button"
            className="md-pill"
            aria-pressed={value.tiebreakAt === games}
            disabled={disabled}
            onClick={() => patch({ tiebreakAt: games })}
          >
            bis {games}
          </button>
        ))}
        <label
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 6,
            fontSize: 'var(--fs-xs)',
            color: 'var(--fg-3)',
          }}
        >
          andere
          <input
            type="number"
            className="md-input md-num"
            min={MIN_GAMES}
            max={MAX_GAMES}
            value={value.tiebreakAt}
            disabled={disabled}
            aria-label="Spiele pro Satz"
            onChange={(event) => {
              const games = Number(event.target.value)
              if (!Number.isFinite(games)) return
              patch({ tiebreakAt: Math.min(MAX_GAMES, Math.max(MIN_GAMES, Math.round(games))) })
            }}
            style={{ width: 62, minHeight: 'var(--hit-target)' }}
          />
        </label>
      </Row>

      <Row label="Entscheidungssatz" hint={decidingHint}>
        {FINAL_SET.map((option) => (
          <button
            key={option.value}
            type="button"
            className="md-pill"
            aria-pressed={value.finalSetMode === option.value}
            disabled={disabled}
            title={option.hint}
            onClick={() => patch({ finalSetMode: option.value })}
          >
            {option.label}
          </button>
        ))}
      </Row>

      <div className="md-hint" style={{ fontSize: 'var(--fs-xs)' }}>
        Gespielt wird: <strong>{matchFormatSummary(value)}</strong>. Die Ergebniseingabe prüft
        gegen genau diese Angaben — ein 6:4 lässt sich in einem Turnier mit Sätzen bis 4 nicht
        eintragen.
      </div>
    </div>
  )
}

function Row({
  label,
  hint,
  children,
}: {
  label: string
  hint: string
  children: React.ReactNode
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--sp-7)', flexWrap: 'wrap' }}>
      <div style={{ width: 190, flex: 'none' }}>
        <div style={{ fontSize: 12.5, fontWeight: 'var(--fw-semibold)' }}>{label}</div>
        <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', lineHeight: 1.35 }}>
          {hint}
        </div>
      </div>
      <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap', alignItems: 'center' }}>
        {children}
      </div>
    </div>
  )
}
