import { useEffect, useRef, type ReactNode } from 'react'

/**
 * Eine Lade, die von unten hereinkommt.
 *
 * Am Telefon ist unten die Stelle, die der Daumen erreicht — ein Menü, das
 * oben aufklappt, ist auf einem Sechs-Zoll-Gerät eine Zumutung. Am Schreibtisch
 * erscheint dieselbe Lade als Feld neben der Schaltfläche; den Unterschied
 * macht das Stylesheet, nicht dieses Bauteil.
 *
 * Bewusst kein <dialog>: dessen Modalität blendet den Rest der Seite für
 * Hilfsmittel aus, und die Lade trägt hier nur eine Auswahl. Escape und der
 * Klick daneben schließen sie, mehr braucht es nicht.
 */
export function Sheet({
  open,
  title,
  onClose,
  children,
}: {
  open: boolean
  title: string
  onClose: () => void
  children: ReactNode
}) {
  const panel = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose])

  // Der Blick landet in der Lade und nicht dort, wo er vorher war: sonst
  // liest ein Screenreader weiter oben vor, während unten die Auswahl steht.
  useEffect(() => {
    if (open) panel.current?.focus()
  }, [open])

  if (!open) return null

  return (
    <div className="md-sheet-layer">
      <button
        type="button"
        className="md-sheet__scrim"
        aria-label="Schließen"
        onClick={onClose}
      />
      <div
        className="md-sheet"
        role="group"
        aria-label={title}
        tabIndex={-1}
        ref={panel}
      >
        <div className="md-sheet__grip" aria-hidden="true" />
        <div className="md-sheet__title">{title}</div>
        <div className="md-sheet__body">{children}</div>
      </div>
    </div>
  )
}
