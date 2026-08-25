/**
 * Das Satzformat einstellen.
 *
 * Drei Fragen entscheiden es — wie viele Sätze, bis wie viele Spiele, und was
 * im entscheidenden Satz gilt. Sie standen hier als drei Reihen mit zusammen
 * neun Schaltflächen und einem Zahlenfeld, und man musste alle drei
 * beantworten, um anzufangen.
 *
 * Jetzt steht vorn, wonach tatsächlich gefragt wird: „wie lange soll ein
 * Match dauern". Drei Antworten decken so gut wie jeden Vereinsnachmittag ab.
 * Wer etwas anderes braucht — Vorteilssatz, Sätze bis 8 —, klappt „Anpassen"
 * auf und findet dort dieselben drei Reihen wie vorher. Nichts ist
 * verschwunden, es steht nur nicht mehr allen im Weg.
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

interface Preset {
  id: string
  label: string
  duration: string
  format: MatchFormat
}

/**
 * Die drei, nach denen ein Nachmittag geplant wird.
 *
 * Beschriftet nach ihrer Dauer und nicht nach ihrer Bauart: „bis 4, ein Satz,
 * Champions-Tiebreak" ist die Antwort auf eine Frage, die niemand stellt. Wie
 * lange es dauert, ist die Frage.
 */
const PRESETS: Preset[] = [
  {
    id: 'kurz',
    label: 'Kurz',
    duration: '~30 min',
    format: { bestOf: 1, finalSetMode: FinalSetMode.Regular, tiebreakAt: 4 },
  },
  {
    id: 'standard',
    label: 'Standard',
    duration: '~60 min',
    format: { bestOf: 3, finalSetMode: FinalSetMode.MatchTiebreak10, tiebreakAt: 6 },
  },
  {
    id: 'lang',
    label: 'Lang',
    duration: '~90 min',
    format: { bestOf: 3, finalSetMode: FinalSetMode.Regular, tiebreakAt: 6 },
  },
]

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

/** Welche der drei Vorlagen genau dieses Format beschreibt — oder keine. */
export function presetOf(format: MatchFormat): string | null {
  return (
    PRESETS.find(
      (preset) =>
        preset.format.bestOf === format.bestOf &&
        preset.format.tiebreakAt === format.tiebreakAt &&
        preset.format.finalSetMode === format.finalSetMode,
    )?.id ?? null
  )
}

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

  const active = presetOf(value)

  // Bei einem einzigen Satz gibt es keinen „letzten" neben anderen — die Wahl
  // bleibt trotzdem stehen, denn sie entscheidet dann, ob überhaupt ein Satz
  // oder nur ein Champions-Tiebreak gespielt wird.
  const decidingHint =
    value.bestOf === 1
      ? 'Bei einem einzigen Satz ist er das ganze Match.'
      : `Nur der ${value.bestOf}. Satz — die davor werden normal gespielt.`

  return (
    <div className="md-format">
      <div className="md-choices" role="group" aria-label="Spieldauer">
        {PRESETS.map((preset) => (
          <button
            key={preset.id}
            type="button"
            className="md-choice"
            aria-pressed={active === preset.id}
            disabled={disabled}
            onClick={() => onChange({ ...preset.format })}
          >
            <span className="md-choice__label">{preset.label}</span>
            <span className="md-choice__sub">{preset.duration}</span>
          </button>
        ))}
      </div>

      <div className="md-hint">
        Gespielt wird: <strong>{matchFormatSummary(value)}</strong>. Die Ergebniseingabe prüft
        gegen genau diese Angaben — ein 6:4 lässt sich in einem Turnier mit Sätzen bis 4 nicht
        eintragen.
      </div>

      {/* Offen, sobald etwas eingestellt ist, das keine der drei Vorlagen
          beschreibt: eine zugeklappte Lade, in der die geltende Einstellung
          steht, verschweigt sie. */}
      <details className="md-details" open={active === null}>
        <summary className="md-details__summary">Anpassen</summary>

        <div className="md-details__body">
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
            <label className="md-inline-field">
              andere
              <input
                type="number"
                className="md-input md-num"
                min={MIN_GAMES}
                max={MAX_GAMES}
                value={value.tiebreakAt}
                disabled={disabled}
                aria-label="Spiele pro Satz"
                // Ein Zahlenfeld gibt nur Zahlen oder eine leere Zeichenkette
                // heraus — Unsinn verwirft der Browser selbst. Hier stand
                // deshalb eine Prüfung auf `Number.isFinite`, die nie
                // ausschlagen konnte; die leere Eingabe wird ohnehin auf die
                // Untergrenze gehoben.
                onChange={(event) =>
                  patch({
                    tiebreakAt: Math.min(
                      MAX_GAMES,
                      Math.max(MIN_GAMES, Math.round(Number(event.target.value))),
                    ),
                  })
                }
                style={{ width: 78 }}
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
        </div>
      </details>
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
    <div className="md-field">
      <div className="md-field__label">{label}</div>
      <span className="md-field__hint">{hint}</span>
      <div className="md-field__control">{children}</div>
    </div>
  )
}
