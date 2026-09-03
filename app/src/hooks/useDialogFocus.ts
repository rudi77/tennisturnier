import { useEffect, useRef } from 'react'

/**
 * Der Fokus in einem Dialog: hinein, drinnen bleiben, und danach zurück.
 *
 * Drei Dinge, die ein `role="dialog"` verspricht und die von Hand nicht von
 * selbst kommen. Ohne sie steht der Fokus beim Öffnen weiter auf der
 * Schaltfläche dahinter — die Tabulatortaste läuft dann durch die Seite unter
 * dem Dialog, ein Screenreader liest dort vor, und beim Schließen ist der
 * Fokus verloren: er landet am Anfang des Dokuments, und wer mit der Tastatur
 * arbeitet, sucht sich seine Stelle wieder.
 *
 * Escape schließt, weil ein Dialog, den man nur mit der Maus loswird, keiner
 * ist.
 */
const FOKUSSIERBAR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

function fokussierbare(wurzel: HTMLElement): HTMLElement[] {
  return [...wurzel.querySelectorAll<HTMLElement>(FOKUSSIERBAR)]
}

export function useDialogFocus<T extends HTMLElement>(open: boolean, onClose: () => void) {
  const panel = useRef<T>(null)

  useEffect(() => {
    if (!open) return

    // Der Dialog steht, wenn dieser Effekt läuft: React setzt Refs, bevor es
    // Effekte ausführt. Eine Prüfung auf null wäre hier eine Zeile, die nie
    // greift — und eine, die niemand je prüfen könnte.
    const dialog = panel.current!

    // Wohin der Fokus zurückgeht. Festgehalten vor dem Verschieben, weil
    // danach der Dialog selbst dort steht.
    const vorher = document.activeElement as HTMLElement | null

    dialog.focus()

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
        return
      }

      if (event.key !== 'Tab') return

      const felder = fokussierbare(dialog)

      if (felder.length === 0) {
        // Nichts zu bedienen: dann bleibt der Fokus, wo er ist, statt hinter
        // den Dialog zu wandern.
        event.preventDefault()
        return
      }

      const erstes = felder[0]!
      const letztes = felder[felder.length - 1]!
      const aktiv = document.activeElement

      if (event.shiftKey && (aktiv === erstes || aktiv === dialog)) {
        event.preventDefault()
        letztes.focus()
      } else if (!event.shiftKey && aktiv === letztes) {
        event.preventDefault()
        erstes.focus()
      }
    }

    document.addEventListener('keydown', onKey)

    return () => {
      document.removeEventListener('keydown', onKey)
      vorher?.focus()
    }
  }, [open, onClose])

  return panel
}
