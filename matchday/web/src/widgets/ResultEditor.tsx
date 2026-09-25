import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import type { MatchView, ResultKind, ResultRequest, SetScore, TournamentView } from '../api'
import { withoutLeadingZeros } from '../format'

/**
 * Die Ergebnismaske: Sätze tippen, fertig. Der Sieger folgt aus den Sätzen;
 * bei Nichtantreten und Aufgabe sagt man, wer weiterkommt. Was die Domäne
 * ablehnt, steht als Satz unter der Maske.
 */
export function ResultEditor({
  view,
  match,
  onClose,
  onSave,
  onClear,
}: {
  view: TournamentView
  match: MatchView
  onClose: () => void
  onSave: (result: ResultRequest) => Promise<void>
  onClear: () => Promise<void>
}) {
  const format = view.format
  const existing = match.score
  const [kind, setKind] = useState<ResultKind>(existing?.outcome === 'Walkover' ? 'Walkover' : existing?.outcome === 'Retirement' ? 'Retired' : 'Played')
  const [sets, setSets] = useState<SetScore[]>(existing && existing.outcome !== 'Walkover' ? existing.sets.map((s) => ({ ...s })) : [{ games1: 0, games2: 0 }])
  const [advancing, setAdvancing] = useState<1 | 2>(existing?.winnerSide ?? 1)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const wins1 = sets.filter((s) => s.games1 > s.games2).length
  const wins2 = sets.filter((s) => s.games2 > s.games1).length
  const winnerSide: 1 | 2 = kind === 'Played' ? (wins1 >= wins2 ? 1 : 2) : advancing
  const target = format.tiebreakAt

  function update(i: number, side: 1 | 2, value: number) {
    setSets((all) => all.map((s, j) => (j === i ? { ...s, [side === 1 ? 'games1' : 'games2']: Math.max(0, Math.min(99, value)) } : s)))
  }

  function updateTiebreak(i: number, value: string) {
    const n = value === '' ? null : Math.max(0, Number(value))
    setSets((all) => all.map((s, j) => (j === i ? { ...s, tiebreakPoints: n } : s)))
  }

  function isTiebreakSet(s: SetScore, index: number) {
    const finalSet = index === format.bestOf - 1 && format.bestOf > 1
    if (finalSet && format.finalSetMode === 'MatchTiebreak10') return false
    const hi = Math.max(s.games1, s.games2)
    const lo = Math.min(s.games1, s.games2)
    return hi === target + 1 && lo === target
  }

  async function save() {
    setBusy(true)
    setError(null)
    const oriented = sets.map((s) => (winnerSide === 1 ? s : { games1: s.games2, games2: s.games1, tiebreakPoints: s.tiebreakPoints }))
    const request: ResultRequest =
      kind === 'Played'
        ? { kind, winnerSide, sets: oriented }
        : kind === 'Walkover'
          ? { kind, winnerSide, sets: [] }
          : {
              kind,
              winnerSide,
              sets: oriented.filter((s) => s.games1 !== s.games2),
              abandonedSet: oriented.find((s) => s.games1 === s.games2) ?? null,
            }
    try {
      await onSave(request)
      onClose()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function clear() {
    setBusy(true)
    setError(null)
    try {
      await onClear()
      onClose()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const setsToWin = Math.floor(format.bestOf / 2) + 1
  const decided = kind === 'Played' && (wins1 === setsToWin || wins2 === setsToWin)

  // Ins Dokument selbst gehängt, wie das Zählfenster: auf der Bühne läge es
  // unter Gespräch und Eingabefeld.
  return createPortal(
    <div className="modal" role="dialog" aria-modal="true" aria-label={`Ergebnis ${match.label}`} onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="modal__box">
        <div className="card__head">
          <h2 className="card__title">{match.label}</h2>
          <button type="button" className="icon" aria-label="Schließen" onClick={onClose}>
            ×
          </button>
        </div>
        <p className="muted">{view.formatText}</p>

        <div className="segmented" role="radiogroup" aria-label="Art des Ergebnisses">
          {(
            [
              ['Played', 'Gespielt'],
              ['Walkover', 'Nicht angetreten'],
              ['Retired', 'Aufgabe'],
            ] as [ResultKind, string][]
          ).map(([value, label]) => (
            <button key={value} type="button" role="radio" aria-checked={kind === value} className={kind === value ? 'on' : ''} onClick={() => setKind(value)}>
              {label}
            </button>
          ))}
        </div>

        {kind !== 'Played' && (
          <div className="field">
            <span className="field__label">Wer kommt weiter?</span>
            <div className="segmented">
              <button type="button" className={advancing === 1 ? 'on' : ''} onClick={() => setAdvancing(1)}>
                {match.side1.name}
              </button>
              <button type="button" className={advancing === 2 ? 'on' : ''} onClick={() => setAdvancing(2)}>
                {match.side2.name}
              </button>
            </div>
          </div>
        )}

        {kind !== 'Walkover' && (
          <table className="sets-table">
            <thead>
              <tr>
                <th className="left"></th>
                {sets.map((_, i) => (
                  <th key={i}>Satz {i + 1}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {([1, 2] as const).map((side) => (
                <tr key={side} className={decided && winnerSide === side ? 'winner' : ''}>
                  <td className="left">{side === 1 ? match.side1.name : match.side2.name}</td>
                  {sets.map((s, i) => (
                    <td key={i}>
                      <input
                        type="number"
                        inputMode="numeric"
                        min={0}
                        max={99}
                        value={side === 1 ? s.games1 : s.games2}
                        // Antippen markiert die Zahl, die nächste Ziffer ersetzt sie.
                        onFocus={(e) => e.currentTarget.select()}
                        onChange={(e) => {
                          const clean = withoutLeadingZeros(e.target.value)
                          if (clean !== e.target.value) e.target.value = clean
                          update(i, side, Number(clean))
                        }}
                        aria-label={`Satz ${i + 1}, ${side === 1 ? match.side1.name : match.side2.name}`}
                      />
                    </td>
                  ))}
                </tr>
              ))}
              <tr className="tiebreak-row">
                <td className="left muted">Tiebreak</td>
                {sets.map((s, i) => (
                  <td key={i}>
                    {isTiebreakSet(s, i) ? (
                      <input type="number" inputMode="numeric" min={0} placeholder="Verlierer" value={s.tiebreakPoints ?? ''} onFocus={(e) => e.currentTarget.select()} onChange={(e) => updateTiebreak(i, e.target.value)} aria-label={`Tiebreak-Punkte des Unterlegenen, Satz ${i + 1}`} />
                    ) : (
                      <span className="muted">–</span>
                    )}
                  </td>
                ))}
              </tr>
            </tbody>
          </table>
        )}

        {kind !== 'Walkover' && (
          <div className="actions actions--start">
            <button type="button" className="button" disabled={sets.length >= format.bestOf} onClick={() => setSets((all) => [...all, { games1: 0, games2: 0 }])}>
              + Satz
            </button>
            <button type="button" className="button" disabled={sets.length <= 1} onClick={() => setSets((all) => all.slice(0, -1))}>
              − Satz
            </button>
            {kind === 'Retired' && <span className="muted">Der laufende Satz bleibt unentschieden stehen.</span>}
          </div>
        )}

        {error && <p className="error">{error}</p>}

        <div className="actions">
          {existing && (
            <button type="button" className="button button--danger" disabled={busy} onClick={() => void clear()}>
              Ergebnis löschen
            </button>
          )}
          <span className="spacer" />
          <button type="button" className="button" onClick={onClose}>
            Abbrechen
          </button>
          <button type="button" className="button button--primary" disabled={busy} onClick={() => void save()}>
            Speichern
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
