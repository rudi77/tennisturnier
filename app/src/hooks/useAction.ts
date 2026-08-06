import { useCallback, useState } from 'react'
import { useToast } from './useToast'

/**
 * Eine Handlung mit Folgen: sperren, ausführen, melden, entsperren.
 *
 * Dieses Muster stand neunmal im Projekt, jedes Mal von Hand — mit derselben
 * try/catch/finally-Kette und derselben Reihenfolge aus Nachladen und Meldung.
 * Neun Abschriften sind nicht bloß Tipparbeit: eine davon vergaß das
 * `finally`, und eine gescheiterte Handlung ließ die Schaltfläche gesperrt
 * zurück.
 *
 * Die Reihenfolge ist Absicht. Erst nachladen, dann melden: die Meldung sagt
 * „ausgelost", und wer sie liest, schaut auf den Bildschirm. Stünde sie vorher,
 * spräche sie über einen Stand, der noch nicht da ist.
 */
export function useAction(): {
  busy: boolean
  run: (label: string, action: () => Promise<unknown>, done?: string) => Promise<void>
} {
  const { show, showError } = useToast()
  const [busy, setBusy] = useState(false)

  const run = useCallback(
    async (label: string, action: () => Promise<unknown>, done?: string) => {
      setBusy(true)
      try {
        await action()
        if (done) show(done)
      } catch (cause) {
        showError(cause, label)
      } finally {
        setBusy(false)
      }
    },
    [show, showError],
  )

  return { busy, run }
}
