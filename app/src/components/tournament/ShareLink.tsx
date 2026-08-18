import { useState } from 'react'
import { useToast } from '../../hooks/useToast'

/**
 * Legt Text in die Zwischenablage und sagt ehrlich, ob es geklappt hat.
 *
 * Zwei Wege, weil der erste öfter fehlt als man denkt: `navigator.clipboard`
 * gibt es nur im sicheren Kontext, und selbst dort scheitert `writeText`, wenn
 * das Fenster gerade nicht den Fokus hat. Der zweite Weg über ein kurzlebiges
 * Textfeld ist veraltet und funktioniert überall.
 *
 * Der Rückgabewert ist der Punkt. Vorher wurde „kopiert" gemeldet, sobald der
 * Aufruf nicht geworfen hatte — und wer danach einfügen wollte, hatte nichts.
 */
async function copyToClipboard(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // Kein Grund zur Meldung: es gibt einen zweiten Weg.
  }

  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.top = '-1000px'
  document.body.appendChild(area)
  area.select()
  area.setSelectionRange(0, text.length)

  try {
    return document.execCommand('copy')
  } catch {
    return false
  } finally {
    document.body.removeChild(area)
  }
}

/**
 * Einen Link weitergeben.
 *
 * Kopieren steht immer da, Teilen nur zusätzlich. Zuerst war es ein Knopf, der
 * je nach Gerät das eine <em>oder</em> das andere tat — und das ging schief:
 * Chrome kennt `navigator.share` auch am Schreibtisch, also öffnete sich dort
 * das Teilen-Blatt des Systems. Wer es zumachte, hatte nichts in der
 * Zwischenablage und bekam auch keine Meldung darüber, weil ein Abbruch kein
 * Fehler ist.
 *
 * Zwei Handlungen, zwei Knöpfe. Der eine tut immer dasselbe, der andere ist ein
 * Angebot — auf dem Handy führt er in die WhatsApp-Gruppe des Vereins, und
 * genau dorthin gehen diese Links.
 *
 * Die Adresse kommt von außen und wird hier nicht mehr gebaut: es sind zwei
 * geworden — der Anmeldelink für die Meldung und der Zuschauerlink für die
 * Live-Ansicht. Sie unterscheiden sich in nichts, was das Teilen anginge.
 */
export function ShareLink({
  url,
  label,
  shareTitle,
  shareText,
  copiedMessage,
  className = 'md-btn md-btn--accent',
}: {
  url: string
  /** Die Beschriftung des Knopfes — „Anmeldelink kopieren". */
  label: string
  shareTitle: string
  shareText: string
  /** Die Meldung nach dem Kopieren. Sie benennt, was in der Ablage liegt. */
  copiedMessage: string
  className?: string
}) {
  const { show, showError } = useToast()
  const [busy, setBusy] = useState(false)

  const canShare = typeof navigator.share === 'function'

  const copy = async () => {
    setBusy(true)
    try {
      const done = await copyToClipboard(url)

      if (done) {
        show(copiedMessage)
      } else {
        showError(
          new Error('Der Browser hat das Kopieren abgelehnt. Der Link lässt sich von Hand markieren.'),
          'Kopieren',
        )
      }
    } finally {
      setBusy(false)
    }
  }

  const share = async () => {
    setBusy(true)
    try {
      await navigator.share({ title: shareTitle, text: shareText, url })
    } catch (cause) {
      // Wer das Teilen-Blatt zumacht, hat sich umentschieden. Das ist kein
      // Fehler und bekommt keine Meldung.
      if (cause instanceof DOMException && cause.name === 'AbortError') return
      showError(cause, 'Teilen')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <button type="button" className={className} disabled={busy} onClick={() => void copy()}>
        {label}
      </button>

      {canShare && (
        <button type="button" className="md-btn" disabled={busy} onClick={() => void share()}>
          Teilen
        </button>
      )}
    </>
  )
}
