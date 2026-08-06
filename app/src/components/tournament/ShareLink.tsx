import { useState } from 'react'
import { useToast } from '../../hooks/useToast'
import { registrationUrl } from '../../hooks/useRoute'

/**
 * Den Anmeldelink weitergeben.
 *
 * Auf dem Handy über das Teilen-Blatt des Systems: dort steht die
 * WhatsApp-Gruppe des Vereins, und genau dorthin geht dieser Link. Ihn erst in
 * die Zwischenablage zu legen und den Benutzer dann selbst eine App suchen zu
 * lassen wäre ein Umweg über zwei zusätzliche Handgriffe.
 *
 * `navigator.share` gibt es nicht überall — am Schreibtisch fast nirgends.
 * Deshalb ist Kopieren kein Notnagel, sondern der gleichwertige zweite Weg, und
 * die Beschriftung sagt vorher, welcher es sein wird.
 *
 * Ein Abbruch im Teilen-Blatt ist kein Fehler: wer es wieder zumacht, hat sich
 * umentschieden und braucht keine Meldung darüber.
 */
export function ShareLink({
  token,
  tournamentName,
  className = 'md-btn md-btn--accent',
}: {
  token: string
  tournamentName: string
  className?: string
}) {
  const { show, showError } = useToast()
  const [busy, setBusy] = useState(false)

  const canShare = typeof navigator.share === 'function'

  const share = async () => {
    const url = registrationUrl(token)
    setBusy(true)
    try {
      if (canShare) {
        await navigator.share({
          title: tournamentName,
          text: `Melde dich zu „${tournamentName}" an:`,
          url,
        })
        return
      }

      await navigator.clipboard.writeText(url)
      show('Anmeldelink kopiert')
    } catch (cause) {
      if (cause instanceof DOMException && cause.name === 'AbortError') return
      showError(cause, 'Teilen')
    } finally {
      setBusy(false)
    }
  }

  return (
    <button type="button" className={className} disabled={busy} onClick={() => void share()}>
      {canShare ? 'Anmeldelink teilen' : 'Anmeldelink kopieren'}
    </button>
  )
}
