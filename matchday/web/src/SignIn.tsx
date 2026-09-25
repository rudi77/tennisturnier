import { useEffect, useRef, useState } from 'react'
import { Mark } from './Mark'

/**
 * Der Anmeldeschirm. Er steht vor dem Gespräch, wenn der Server eine
 * Anmeldung verlangt — Mitschauen und Eintragen erreicht er nie, denn das
 * sind eigene Wege und bleiben offen (ADR-0019, ADR-0030).
 *
 * Den Knopf zeichnet Google selbst: Ein nachgebauter dürfte die Marke gar
 * nicht tragen, und die Skript-Einbindung brauchte es ohnehin.
 */

const SCRIPT = 'https://accounts.google.com/gsi/client'

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: { client_id: string; callback: (response: { credential: string }) => void }) => void
          renderButton: (parent: HTMLElement, options: Record<string, unknown>) => void
        }
      }
    }
  }
}

/** Das Skript einmal laden, auch wenn zwei Aufrufe gleichzeitig kommen. */
let geladen: Promise<void> | null = null

function skriptLaden(): Promise<void> {
  if (geladen) return geladen

  geladen = new Promise<void>((fertig, fehlschlag) => {
    const vorhanden = document.querySelector<HTMLScriptElement>(`script[src="${SCRIPT}"]`)
    if (vorhanden) {
      fertig()
      return
    }

    const script = document.createElement('script')
    script.src = SCRIPT
    script.async = true
    script.onload = () => fertig()
    script.onerror = () => fehlschlag(new Error('Google ist nicht erreichbar.'))
    document.head.appendChild(script)
  })

  return geladen
}

export function SignIn({
  clientId,
  onToken,
  hinweis = '',
}: {
  clientId: string
  onToken: (token: string) => void
  /** Was nach der Anmeldung schiefging — etwa ein Konto, das nicht freigegeben ist. */
  hinweis?: string
}) {
  const knopf = useRef<HTMLDivElement>(null)
  const [fehler, setFehler] = useState('')

  useEffect(() => {
    let abgebrochen = false

    skriptLaden()
      .then(() => {
        if (abgebrochen || !knopf.current) return
        const google = window.google
        if (!google) {
          setFehler('Google ist nicht erreichbar. Ohne Anmeldung geht es hier nicht weiter.')
          return
        }

        google.accounts.id.initialize({
          client_id: clientId,
          callback: (antwort) => onToken(antwort.credential),
        })
        google.accounts.id.renderButton(knopf.current, {
          theme: 'outline',
          size: 'large',
          text: 'signin_with',
          locale: 'de',
        })
      })
      .catch((e: Error) => {
        if (!abgebrochen) setFehler(e.message)
      })

    return () => {
      abgebrochen = true
    }
  }, [clientId, onToken])

  return (
    <div className="signin">
      <div className="signin__box">
        <Mark />
        <h1 className="signin__title">Ein Turnier mit Freunden</h1>
        <p className="signin__text">
          Zum Anlegen und Verwalten von Turnieren brauchst du eine Anmeldung. Zum <strong>Mitschauen</strong> und <strong>Eintragen</strong> nicht —
          dafür genügt der Link, den du bekommen hast.
        </p>
        <div ref={knopf} className="signin__button" />
        {(fehler || hinweis) && <p className="signin__error">{fehler || hinweis}</p>}
      </div>
    </div>
  )
}
