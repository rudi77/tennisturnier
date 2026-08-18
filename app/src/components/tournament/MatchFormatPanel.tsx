/**
 * Das Satzformat eines bestehenden Turniers.
 *
 * Es steht beim Anlegen schon zur Wahl, und trotzdem gehört es hierher: die
 * Entscheidung fällt selten am Anfang. Sie fällt, wenn die Meldungen da sind
 * und jemand nachrechnet — achtzehn Paarungen, zwei Plätze, um sechs müssen
 * die Platzwarte zusperren. Wer dann kein Feld dafür findet, spielt entweder
 * bis in die Nacht oder legt das Turnier neu an.
 *
 * Ab der Auslosung ist Schluss damit, und das steht hier auch: das Format ist
 * in den Snapshot eingefroren, und jedes bereits eingetragene Ergebnis wurde
 * gegen genau dieses Format geprüft.
 */

import { useEffect, useState } from 'react'
import { tournaments as tournamentApi } from '../../api/endpoints'
import { TournamentState, type MatchFormat, type TournamentDetail } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { matchFormatSummary } from '../../lib/matchFormat'
import { MatchFormatPicker } from './MatchFormatPicker'

/** Ab hier steht das Format im eingefrorenen Snapshot (Tournament.IsFrozen). */
const FROZEN: TournamentState[] = [
  TournamentState.DrawGenerated,
  TournamentState.InProgress,
  TournamentState.Completed,
  TournamentState.Abandoned,
]

export function MatchFormatPanel({
  tournament,
  onChanged,
}: {
  tournament: TournamentDetail
  onChanged: () => Promise<void>
}) {
  const { busy, run } = useAction(onChanged)
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState<MatchFormat>(tournament.effectiveMatchFormat)

  // Der Entwurf folgt dem Turnier — sonst zeigte die Maske nach einer
  // Änderung von anderswo den Stand von vorhin und schriebe ihn beim
  // nächsten Speichern zurück.
  useEffect(() => setDraft(tournament.effectiveMatchFormat), [tournament.effectiveMatchFormat])

  const frozen = FROZEN.includes(tournament.state)
  const dirty = JSON.stringify(draft) !== JSON.stringify(tournament.effectiveMatchFormat)

  return (
    <div className="md-panel" style={{ padding: 'var(--sp-8)', marginTop: 'var(--sp-8)' }}>
      <div
        style={{
          display: 'flex',
          alignItems: 'baseline',
          justifyContent: 'space-between',
          gap: 'var(--sp-5)',
          flexWrap: 'wrap',
        }}
      >
        <div>
          <div className="md-eyebrow">Satzformat</div>
          <div style={{ fontSize: 'var(--fs-md)', marginTop: 3 }}>
            {matchFormatSummary(tournament.effectiveMatchFormat)}
          </div>
        </div>

        {!frozen && (
          <button type="button" className="md-btn" onClick={() => setOpen((it) => !it)}>
            {open ? 'Schließen' : 'Ändern'}
          </button>
        )}
      </div>

      {frozen ? (
        <div className="md-hint" style={{ marginTop: 'var(--sp-5)' }}>
          Mit der Auslosung eingefroren. Jedes eingetragene Ergebnis wurde gegen dieses Format
          geprüft — änderte es sich jetzt, wäre ein gültiges Ergebnis von vorhin plötzlich keines
          mehr.
        </div>
      ) : (
        open && (
          <div style={{ marginTop: 'var(--sp-7)' }}>
            <MatchFormatPicker value={draft} onChange={setDraft} disabled={busy} />

            <div className="md-flow__row" style={{ marginTop: 'var(--sp-7)' }}>
              <button
                type="button"
                className="md-btn md-btn--accent"
                disabled={busy || !dirty}
                onClick={() =>
                  void run(
                    'Satzformat',
                    () => tournamentApi.setMatchFormat(tournament.id, draft),
                    `Satzformat: ${matchFormatSummary(draft)}`,
                  )
                }
              >
                Übernehmen
              </button>

              {/* Nur, wenn überhaupt etwas eingestellt ist — sonst führte der
                  Knopf zurück zu dem, was ohnehin gilt. */}
              {tournament.matchFormat && (
                <button
                  type="button"
                  className="md-btn"
                  disabled={busy}
                  onClick={() =>
                    void run(
                      'Satzformat',
                      () => tournamentApi.setMatchFormat(tournament.id, null),
                      'Es gilt wieder das Format der Vorlage',
                    )
                  }
                >
                  Zurück zur Vorlage
                </button>
              )}
            </div>
          </div>
        )
      )}
    </div>
  )
}
