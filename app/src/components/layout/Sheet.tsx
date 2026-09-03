import { type ReactNode } from 'react'
import { useDialogFocus } from '../../hooks/useDialogFocus'

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
  // Der Blick landet in der Lade und nicht dort, wo er vorher war: sonst
  // liest ein Screenreader weiter oben vor, während unten die Auswahl steht.
  // Und beim Schließen geht er dorthin zurück, wo er herkam — auf die
  // Schaltfläche, die die Lade geöffnet hat.
  const panel = useDialogFocus<HTMLDivElement>(open, onClose)

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
