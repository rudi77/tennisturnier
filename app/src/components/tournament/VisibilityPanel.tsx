import { useState } from 'react'
import { ShareLink } from './ShareLink'
import { tournaments as tournamentApi } from '../../api/endpoints'
import { useToast } from '../../hooks/useToast'
import { publicUrl } from '../../hooks/useRoute'
import type { TournamentDetail } from '../../api/types'

/**
 * Wer außer den Mitgliedern zusehen darf.
 *
 * Ein Turnier ist zuerst eine Gruppe: seine Mitglieder sehen es, sonst niemand
 * (ADR-0012). Der Clubhaus-Monitor und der Link, der in der Vereinsgruppe
 * herumgeht, bleiben möglich — sie sind eine Entscheidung geworden.
 *
 * Der Zuschauerlink steht erst da, wenn er auch trägt. Ein Link, den man
 * kopieren kann und der beim Empfänger auf einen Hinweis führt, wäre schlimmer
 * als keiner.
 */
export function VisibilityPanel({
  tournament,
  onChanged,
}: {
  tournament: TournamentDetail
  onChanged: () => void
}) {
  const { show, showError } = useToast()
  const [busy, setBusy] = useState(false)

  const umschalten = async (isPublic: boolean) => {
    setBusy(true)
    try {
      await tournamentApi.setVisibility(tournament.id, { isPublic })
      onChanged()
      show(
        isPublic
          ? 'Öffentlich — jeder mit dem Zuschauerlink sieht Spielplan und Ergebnisse'
          : 'Privat — nur noch Mitglieder sehen das Turnier',
      )
    } catch (cause) {
      showError(cause, 'Sichtbarkeit')
    } finally {
      setBusy(false)
    }
  }

  const url = publicUrl(tournament.id)

  return (
    <div className="md-panel" style={{ padding: 'var(--sp-8)', marginBottom: 'var(--sp-6)' }}>
      <div style={{ fontSize: 'var(--fs-lg)', fontWeight: 'var(--fw-bold)', marginBottom: 3 }}>
        Wer zusehen darf
      </div>
      <div className="md-hint" style={{ marginBottom: 'var(--sp-7)' }}>
        Mitglieder sehen Spielplan und Ergebnisse immer. Öffentlich heißt: auch jeder andere, der
        die Adresse hat — für den Aushang im Vereinsheim oder den Monitor im Clubhaus.
      </div>

      <div className="md-field__control" role="group" aria-label="Sichtbarkeit">
        <button
          type="button"
          className="md-pill"
          aria-pressed={!tournament.isPublic}
          disabled={busy}
          onClick={() => void umschalten(false)}
        >
          Privat
        </button>
        <button
          type="button"
          className="md-pill"
          aria-pressed={tournament.isPublic}
          disabled={busy}
          onClick={() => void umschalten(true)}
        >
          Öffentlich
        </button>
      </div>

      {tournament.isPublic && (
        <div
          style={{
            display: 'flex',
            gap: 'var(--sp-4)',
            alignItems: 'center',
            flexWrap: 'wrap',
            marginTop: 'var(--sp-7)',
          }}
        >
          <input
            className="md-input"
            readOnly
            value={url}
            aria-label="Zuschauerlink"
            style={{ flex: '1 1 260px' }}
            onFocus={(event) => event.currentTarget.select()}
          />
          <ShareLink
            url={url}
            label="Zuschauerlink kopieren"
            shareTitle={tournament.name}
            shareText={`Live dabei bei „${tournament.name}":`}
            copiedMessage="Zuschauerlink kopiert"
            className="md-btn"
          />
        </div>
      )}
    </div>
  )
}
