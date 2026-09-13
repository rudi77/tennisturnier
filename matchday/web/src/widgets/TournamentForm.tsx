import { useState } from 'react'
import type { Discipline, FinalSetMode, Mode, TournamentView, UpdateBody } from '../api'

/**
 * Der Rahmen eines Turniers als Formularstand. Derselbe Stand, egal ob gerade
 * ein Turnier entsteht oder ein bestehendes geändert wird — so gibt es das
 * Formular einmal und nicht zweimal.
 */
export interface Draft {
  name: string
  /** YYYY-MM-DD, leer heißt: kein Datum. */
  date: string
  location: string
  mode: Mode
  discipline: Discipline
  bestOf: number
  finalSetMode: FinalSetMode
  tiebreakAt: number
}

export const emptyDraft: Draft = {
  name: '',
  date: '',
  location: '',
  mode: 'Knockout',
  discipline: 'Singles',
  bestOf: 3,
  finalSetMode: 'MatchTiebreak10',
  tiebreakAt: 6,
}

export function draftOf(view: TournamentView): Draft {
  return {
    name: view.name,
    date: view.date ?? '',
    location: view.location ?? '',
    mode: view.mode,
    discipline: view.discipline,
    bestOf: view.format.bestOf,
    finalSetMode: view.format.finalSetMode,
    tiebreakAt: view.format.tiebreakAt,
  }
}

/** Was sich geändert hat — und nur das. Leer heißt: nichts zu tun. */
export function changesBetween(before: Draft, after: Draft): UpdateBody {
  const body: UpdateBody = {}
  if (after.name.trim() !== before.name) body.name = after.name.trim()
  if (after.date !== before.date) {
    if (after.date === '') body.clearDate = true
    else body.date = after.date
  }
  if (after.location.trim() !== before.location) {
    if (after.location.trim() === '') body.clearLocation = true
    else body.location = after.location.trim()
  }
  if (after.mode !== before.mode) body.mode = after.mode
  if (after.discipline !== before.discipline) body.discipline = after.discipline
  if (after.bestOf !== before.bestOf || after.finalSetMode !== before.finalSetMode || after.tiebreakAt !== before.tiebreakAt) {
    body.format = { bestOf: after.bestOf, finalSetMode: after.finalSetMode, tiebreakAt: after.tiebreakAt }
  }
  return body
}

const finalSetLabels: [FinalSetMode, string][] = [
  ['MatchTiebreak10', 'Match-Tiebreak bis 10'],
  ['Regular', 'Ganz normaler Satz'],
  ['Advantage', 'Ohne Tiebreak durchspielen'],
]

/**
 * Name, Datum, Ort, Disziplin, Modus, Satzformat. Was nach der Auslosung fest
 * ist, steht hier gesperrt und mit dem Grund daneben — ein Feld, das nichts
 * annimmt, ohne zu sagen warum, ist schlimmer als keines.
 */
export function TournamentForm({
  draft,
  onChange,
  locked = false,
  disciplineLocked = false,
  extrasOpen = true,
}: {
  draft: Draft
  onChange: (draft: Draft) => void
  locked?: boolean
  disciplineLocked?: boolean
  extrasOpen?: boolean
}) {
  const [open, setOpen] = useState(extrasOpen)
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => onChange({ ...draft, [key]: value })

  return (
    <div className="form">
      <label className="field">
        <span className="field__label">Name</span>
        <input value={draft.name} onChange={(e) => set('name', e.target.value)} placeholder="Samstagsrunde" />
      </label>

      {!open ? (
        <button type="button" className="button button--quiet button--wrap" onClick={() => setOpen(true)}>
          Mehr einstellen — Datum, Ort, Einzel oder Doppel, Modus, Sätze
        </button>
      ) : (
        <>
          <div className="form__row">
            <label className="field">
              <span className="field__label">Datum</span>
              <input type="date" value={draft.date} onChange={(e) => set('date', e.target.value)} />
            </label>
            <label className="field">
              <span className="field__label">Ort</span>
              <input value={draft.location} onChange={(e) => set('location', e.target.value)} placeholder="Tennisclub" />
            </label>
          </div>

          <Choice
            label="Einzel oder Doppel"
            value={draft.discipline}
            options={[
              ['Singles', 'Einzel'],
              ['Doubles', 'Doppel'],
            ]}
            disabled={locked || disciplineLocked}
            note={
              locked
                ? 'Nach der Auslosung fest.'
                : disciplineLocked
                  ? 'Erst die Teilnehmerliste leeren — ein Einzelname ist kein Team.'
                  : draft.discipline === 'Doubles'
                    ? 'Ein Teilnehmer ist ein Team: „Anna / Tom“.'
                    : undefined
            }
            onPick={(value) => set('discipline', value)}
          />

          <Choice
            label="Modus"
            value={draft.mode}
            options={[
              ['Knockout', 'K.o.'],
              ['RoundRobin', 'Jeder gegen jeden'],
            ]}
            disabled={locked}
            note={
              locked
                ? 'Nach der Auslosung fest.'
                : draft.mode === 'Knockout'
                  ? 'Wer verliert, ist draußen. Überzählige Plätze werden Freilose.'
                  : 'Jeder spielt gegen jeden, die Tabelle entscheidet.'
            }
            onPick={(value) => set('mode', value)}
          />

          <Choice
            label="Sätze"
            value={draft.bestOf}
            options={[
              [1, 'Ein Satz'],
              [3, 'Best of 3'],
              [5, 'Best of 5'],
            ]}
            disabled={locked}
            onPick={(value) => set('bestOf', value)}
          />

          {draft.bestOf > 1 && (
            <label className="field">
              <span className="field__label">Letzter Satz</span>
              <select value={draft.finalSetMode} disabled={locked} onChange={(e) => set('finalSetMode', e.target.value as FinalSetMode)}>
                {finalSetLabels.map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
          )}

          <label className="field">
            <span className="field__label">Ein Satz geht bis</span>
            <input
              type="number"
              min={1}
              max={12}
              value={draft.tiebreakAt}
              disabled={locked}
              onChange={(e) => set('tiebreakAt', Math.max(1, Math.min(12, Number(e.target.value) || 1)))}
            />
            <span className="field__note">Spiele — üblich 6, kurze Sätze 4. Bei Gleichstand entscheidet der Tiebreak.</span>
          </label>
        </>
      )}
    </div>
  )
}

/** Eine Reihe Knöpfe, von denen einer an ist. */
function Choice<T extends string | number>({
  label,
  value,
  options,
  onPick,
  disabled = false,
  note,
}: {
  label: string
  value: T
  options: [T, string][]
  onPick: (value: T) => void
  disabled?: boolean
  note?: string
}) {
  return (
    <div className="field">
      <span className="field__label">{label}</span>
      <div className="segmented" role="radiogroup" aria-label={label}>
        {options.map(([candidate, text]) => (
          <button
            key={candidate}
            type="button"
            role="radio"
            aria-checked={candidate === value}
            className={candidate === value ? 'on' : ''}
            disabled={disabled}
            onClick={() => onPick(candidate)}
          >
            {text}
          </button>
        ))}
      </div>
      {note && <span className="field__note">{note}</span>}
    </div>
  )
}
