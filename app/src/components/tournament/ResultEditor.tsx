import { useMemo, useState } from 'react'
import { matches as matchApi } from '../../api/endpoints'
import { MatchOutcome, type MatchDetail, type MatchFormat, type SetScore } from '../../api/types'
import { outcomeLabel, sideName } from '../../lib/labels'
import {
  isMatchTiebreakSet,
  maxGamesOf,
  openSetCount,
  setLabel,
  whyNotSaveable,
} from '../../lib/matchFormat'
import { ScoreStepper } from '../core/ScoreStepper'
import { useToast } from '../../hooks/useToast'

type Grid = [number, number][]

function initialGrid(match: MatchDetail, format: MatchFormat): Grid {
  const grid: Grid = Array.from({ length: format.bestOf }, () => [0, 0] as [number, number])
  match.score?.completedSets.forEach((set, index) => {
    if (index < format.bestOf) grid[index] = [set.games1, set.games2]
  })
  return grid
}

/**
 * Ergebniseingabe.
 *
 * Der Ausgang steht bewusst unter dem Spielstand und nicht darüber: er bestimmt,
 * welche Angaben überhaupt gebraucht werden. Ein Walkover hat keinen
 * Spielstand, eine Aufgabe einen unvollständigen — und beide brauchen die
 * Angabe, *wen* es betrifft, sonst weiß die Domäne nicht, wer weiterkommt.
 *
 * Die Maske richtet sich nach dem Satzformat und bot einmal stur drei Spalten
 * an. Das ging zweifach schief: ein Match über zwei Gewinnsätze ist nach
 * 6:0, 6:0 vorbei — ein dritter Satz wird zu Recht abgewiesen —, und der
 * dritte ist in der Vorgabe gar kein Satz, sondern ein Match-Tiebreak bis 10.
 * Ein 6:0 dort war ebenfalls ungültig, und der Stepper reichte nicht einmal
 * bis 10.
 */
export function ResultEditor({
  match,
  matchLabel,
  meta,
  format,
  nextRoundName,
  onClose,
  onSaved,
}: {
  match: MatchDetail
  matchLabel: string
  meta: string
  /** Das Satzformat dieser Phase — es bestimmt, wie viele Spalten es gibt. */
  format: MatchFormat
  /** Name der Folgerunde, in der die WinnerOf-Refs aufgelöst werden. */
  nextRoundName: string | null
  onClose: () => void
  onSaved: () => void | Promise<void>
}) {
  const { show, showError } = useToast()

  const [grid, setGrid] = useState<Grid>(() => initialGrid(match, format))
  const [outcome, setOutcome] = useState<MatchOutcome>(match.score?.outcome ?? MatchOutcome.Normal)
  const [affectedSide, setAffectedSide] = useState<number>(1)
  const [saving, setSaving] = useState(false)

  const names = [
    sideName(match.side1.participantName, match.side1.origin),
    sideName(match.side2.participantName, match.side2.origin),
  ]

  const needsAffectedSide = outcome !== MatchOutcome.Normal && outcome !== MatchOutcome.Bye

  // Nur die Sätze, die es geben kann: der nächste erscheint, wenn der vorige
  // gespielt und das Match noch offen ist. Danach ist Schluss — ein Satz nach
  // dem entscheidenden ist kein Zug, den die Maske anbieten darf.
  const visibleSets = useMemo(() => openSetCount(format, grid), [format, grid])
  const playedSets = useMemo(
    () => grid.slice(0, visibleSets).filter(([a, b]) => a > 0 || b > 0),
    [grid, visibleSets],
  )

  const problem = useMemo(
    () => (outcome === MatchOutcome.Normal ? whyNotSaveable(format, grid.slice(0, visibleSets)) : null),
    [format, grid, visibleSets, outcome],
  )

  const bump = (setIndex: number, side: 0 | 1, next: number) => {
    setGrid((current) =>
      current.map((row, index) =>
        index === setIndex
          ? ((side === 0 ? [next, row[1]] : [row[0], next]) as [number, number])
          : row,
      ),
    )
  }

  const save = async () => {
    setSaving(true)
    try {
      const sets: SetScore[] = playedSets.map(([games1, games2]) => ({
        games1,
        games2,
        tiebreakPoints: null,
      }))

      await matchApi.recordResult(match.id, {
        outcome,
        // Ein Walkover hat keinen Spielstand — dann bleibt das Feld leer,
        // statt Nullen zu schicken, die die Domäne als gespielte Sätze läse.
        sets: outcome === MatchOutcome.Walkover ? null : sets,
        abandonedSet: null,
        affectedSide: needsAffectedSide ? affectedSide : null,
      })

      show(
        nextRoundName
          ? `Ergebnis gespeichert · ${matchLabel} — Refs in ${nextRoundName} aufgelöst, Projektion neu gebaut`
          : `Ergebnis gespeichert · ${matchLabel}`,
      )
      await onSaved()
      onClose()
    } catch (cause) {
      showError(cause, 'Ergebnis')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="md-scrim" role="dialog" aria-modal="true" aria-label="Ergebnis erfassen">
      <div
        style={{
          background: 'var(--surface)',
          borderRadius: 'var(--radius-xl)',
          width: 440,
          maxWidth: '100%',
          maxHeight: '90vh',
          overflowY: 'auto',
          boxShadow: 'var(--shadow-modal)',
        }}
      >
        <div style={{ padding: 'var(--sp-8) 18px', borderBottom: '1px solid var(--line-soft)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 10 }}>
            <div style={{ fontSize: 'var(--fs-md)', fontWeight: 'var(--fw-bold)' }}>
              Ergebnis erfassen
            </div>
            <span className="md-num" style={{ fontSize: 'var(--fs-tiny)', color: 'var(--fg-3)' }}>
              {matchLabel}
            </span>
          </div>
          <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--fg-2)', marginTop: 3 }}>{meta}</div>
        </div>

        <div style={{ padding: 18 }}>
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: `1fr repeat(${visibleSets}, auto)`,
              gap: 'var(--sp-5)',
              alignItems: 'center',
            }}
          >
            <div className="md-eyebrow">Spieler</div>
            {Array.from({ length: visibleSets }, (_, index) => (
              <div key={index} className="md-eyebrow" style={{ textAlign: 'center', width: 64 }}>
                {setLabel(format, index)}
              </div>
            ))}

            {names.map((name, side) => (
              <Row
                key={side}
                name={name}
                grid={grid}
                visibleSets={visibleSets}
                format={format}
                side={side as 0 | 1}
                onBump={bump}
                disabled={outcome === MatchOutcome.Walkover}
              />
            ))}
          </div>

          {isMatchTiebreakSet(format, visibleSets - 1) && (
            <div className="md-hint" style={{ marginTop: 'var(--sp-5)' }}>
              Der Entscheidungssatz ist ein Match-Tiebreak: bis 10, mit zwei Punkten Vorsprung.
            </div>
          )}

          <div style={{ marginTop: 'var(--sp-8)' }}>
            <div className="md-eyebrow" style={{ marginBottom: 7 }}>
              Ausgang
            </div>
            <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>
              {[
                MatchOutcome.Normal,
                MatchOutcome.Retirement,
                MatchOutcome.Walkover,
                MatchOutcome.Disqualification,
              ].map((value) => (
                <button
                  key={value}
                  type="button"
                  className="md-pill"
                  aria-pressed={outcome === value}
                  onClick={() => setOutcome(value)}
                >
                  {outcomeLabel[value]}
                </button>
              ))}
            </div>
          </div>

          {needsAffectedSide && (
            <div style={{ marginTop: 'var(--sp-7)' }}>
              <div className="md-eyebrow" style={{ marginBottom: 7 }}>
                Betroffene Seite
              </div>
              <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>
                {names.map((name, index) => (
                  <button
                    key={name + index}
                    type="button"
                    className="md-pill"
                    aria-pressed={affectedSide === index + 1}
                    onClick={() => setAffectedSide(index + 1)}
                    style={{ maxWidth: 190, overflow: 'hidden', textOverflow: 'ellipsis' }}
                  >
                    {name}
                  </button>
                ))}
              </div>
              <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--fg-3)', marginTop: 6 }}>
                {outcome === MatchOutcome.Retirement
                  ? 'Wer aufgegeben hat — die andere Seite kommt weiter.'
                  : 'Wer nicht angetreten ist beziehungsweise disqualifiziert wurde.'}
              </div>
            </div>
          )}

          <div
            style={{
              marginTop: 'var(--sp-7)',
              fontSize: 'var(--fs-xs)',
              color: 'var(--fg-2)',
              lineHeight: 'var(--lh-relaxed)',
              background: 'var(--surface-muted)',
              border: '1px solid var(--line-soft)',
              borderRadius: 'var(--radius)',
              padding: 'var(--sp-5) var(--sp-6)',
            }}
          >
            {nextRoundName
              ? `Speichern löst die WinnerOf-Refs in ${nextRoundName} auf und baut die Projektion neu. Eine Korrektur später ist ein eigener Use Case mit Audit-Eintrag und kann an einem bereits gespielten Folgematch scheitern.`
              : 'Letzte Runde — mit dem Ergebnis wechselt das Turnier nach Completed.'}
          </div>

          {/*
            Dieselbe Aussage wie die Domäne, nur vorher. Wer auf „Speichern"
            drückt, soll nicht erst vom Server erfahren, dass ein Satz fehlt —
            und eine gesperrte Schaltfläche ohne Grund wäre eine Sackgasse.
          */}
          {problem && (
            <div
              className="md-hint"
              role="status"
              style={{ marginTop: 'var(--sp-5)', fontWeight: 'var(--fw-semibold)' }}
            >
              {problem}
            </div>
          )}
        </div>

        <div
          style={{
            padding: 'var(--sp-7) 18px',
            background: 'var(--surface-footer)',
            borderTop: '1px solid var(--line-soft)',
            display: 'flex',
            gap: 'var(--sp-4)',
            justifyContent: 'flex-end',
          }}
        >
          <button
            type="button"
            className="md-btn"
            onClick={onClose}
            style={{ minHeight: 'var(--hit-target)' }}
          >
            Abbrechen
          </button>
          <button
            type="button"
            className="md-btn md-btn--primary"
            onClick={() => void save()}
            disabled={saving || problem !== null}
            title={problem ?? undefined}
            style={{ minHeight: 'var(--hit-target)', fontWeight: 'var(--fw-bold)' }}
          >
            {saving ? 'Speichert …' : 'Speichern & propagieren'}
          </button>
        </div>
      </div>
    </div>
  )
}

function Row({
  name,
  grid,
  visibleSets,
  format,
  side,
  onBump,
  disabled,
}: {
  name: string
  grid: Grid
  visibleSets: number
  format: MatchFormat
  side: 0 | 1
  onBump: (setIndex: number, side: 0 | 1, next: number) => void
  disabled: boolean
}) {
  return (
    <>
      <div
        style={{
          fontSize: 'var(--fs-base)',
          fontWeight: 'var(--fw-semibold)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        }}
        title={name}
      >
        {name}
      </div>
      {grid.slice(0, visibleSets).map((row, setIndex) => (
        <div key={setIndex} style={{ opacity: disabled ? 0.4 : 1, pointerEvents: disabled ? 'none' : 'auto' }}>
          <ScoreStepper
            label={`${name}, ${setLabel(format, setIndex)}`}
            value={row[side]}
            max={maxGamesOf(format, setIndex)}
            onChange={(next) => onBump(setIndex, side, next)}
          />
        </div>
      ))}
    </>
  )
}
