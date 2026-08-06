import { useCallback, useState } from 'react'
import { useToast } from './useToast'

/**
 * Eine Handlung mit Folgen: sperren, ausführen, nachladen, melden, entsperren.
 *
 * Dieses Muster stand neunmal im Projekt, jedes Mal von Hand — mit derselben
 * try/catch/finally-Kette. Neun Abschriften sind nicht bloß Tipparbeit: eine
 * davon vergaß das `finally` und ließ die Schaltfläche nach einem Fehler
 * gesperrt zurück.
 *
 * Das Nachladen gehört dem Hook und nicht dem Aufrufer. Es dem Aufrufer zu
 * überlassen war der erste Versuch, und er ging sofort schief: von vier
 * Zustandsübergängen im Ablauf-Screen luden zwei nach und zwei nicht. Der
 * Unterschied war unsichtbar — beide meldeten Erfolg, nur zeigte die eine
 * Hälfte danach den Stand von vorher, und der nächste Klick lief in einen
 * Konflikt. Was man vergessen kann, vergisst man.
 *
 * Die Reihenfolge ist Absicht. Erst nachladen, dann melden: die Meldung sagt
 * „ausgelost", und wer sie liest, schaut auf den Bildschirm. Stünde sie vorher,
 * spräche sie über einen Stand, der noch nicht da ist.
 */
export function useAction(reload?: () => Promise<unknown>): {
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
        if (reload) await reload()
        if (done) show(done)
      } catch (cause) {
        showError(cause, label)
      } finally {
        setBusy(false)
      }
    },
    [reload, show, showError],
  )

  return { busy, run }
}
