import { ApiError } from '../../api/client'

/** Ladeanzeige — bewusst wortkarg, sie steht oft nur Millisekunden. */
export function Loading({ label = 'Lädt …' }: { label?: string }) {
  return (
    <div className="md-empty" role="status">
      {label}
    </div>
  )
}

/** Ein leerer Zustand mit dem Grund, nicht nur mit dem Befund. */
export function Empty({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="md-empty">
      <div style={{ fontWeight: 'var(--fw-semibold)', color: 'var(--fg)' }}>{title}</div>
      {hint && <div style={{ marginTop: 6, maxWidth: 460, marginInline: 'auto' }}>{hint}</div>}
    </div>
  )
}

/**
 * Ein Fehler.
 *
 * 404 wird nicht als „nicht vorhanden" behauptet: die API antwortet auch dann
 * mit 404, wenn die Ressource zu einem fremden Turnier gehört (ADR-0004). Die
 * Oberfläche darf den Unterschied nicht erfinden.
 */
export function ErrorBlock({ error, onRetry }: { error: Error; onRetry?: () => void }) {
  const api = error instanceof ApiError ? error : null

  const title = api?.isNotFound
    ? 'Nicht gefunden oder außerhalb der eigenen Turniere'
    : api?.isUnauthorized
      ? 'Keine Berechtigung'
      : api?.isConflict
        ? 'Zwischenzeitlich geändert'
        : 'Konnte nicht geladen werden'

  const hint = api?.isUnauthorized
    ? 'Die Rollen vergibt die Anwendung, nicht der IdP. Ein Turnier sieht nur, wer eine Rolle daran hat.'
    : api?.isNotFound
      ? 'Die API unterscheidet beides bewusst nicht, damit die Fehlermeldung nicht verrät, was es zu sehen gäbe.'
      : error.message

  return (
    <div className="md-empty">
      <div style={{ fontWeight: 'var(--fw-semibold)', color: 'var(--fg)' }}>{title}</div>
      <div style={{ marginTop: 6, maxWidth: 520, marginInline: 'auto' }}>{hint}</div>
      {onRetry && (
        <button type="button" className="md-btn" style={{ marginTop: 'var(--sp-8)' }} onClick={onRetry}>
          Erneut versuchen
        </button>
      )}
    </div>
  )
}
