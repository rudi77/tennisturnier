import { useCallback, useEffect, useState } from 'react'
import { ChatScreen } from './ChatScreen'
import { PublicScreen } from './PublicScreen'
import { ScorerScreen } from './ScorerScreen'
import { SignIn } from './SignIn'
import { api } from './api'
import { ABGEMELDET, type AuthConfig, type Konto } from './auth'

/**
 * Drei Adressen, kein Router: `?t=<token>` ist der Mitschau-Link für alle,
 * `?s=<token>` der Eintragen-Link für Mitspieler, `?a=<token>` der
 * Verwalterlink. Alles andere ist das Gespräch.
 *
 * Davor liegt die Frage, ob eine Anmeldung verlangt wird. Sie gilt nicht für
 * das Mitschauen: Zuschauer haben kein Konto und sollen keins brauchen
 * (ADR-0016), und der Server hält diesen Weg entsprechend offen (ADR-0019).
 */
export function App() {
  const [route, setRoute] = useState(read)
  const [auth, setAuth] = useState<AuthConfig | null>(null)
  // undefined heißt: noch nicht gefragt; null: keine Sitzung.
  const [konto, setKonto] = useState<Konto | null | undefined>(undefined)
  const [fehler, setFehler] = useState('')

  useEffect(() => {
    const onPop = () => setRoute(read())
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  // Trägt die Sitzung nicht mehr, meldet die API das einmal — und hier landet
  // man wieder auf der Anmeldung statt in einer Reihe von 401ern.
  useEffect(() => {
    const abmelden = () => setKonto(null)
    window.addEventListener(ABGEMELDET, abmelden)
    return () => window.removeEventListener(ABGEMELDET, abmelden)
  }, [])

  // Der Mitschau-Weg fragt gar nicht erst: Er braucht die Antwort nicht, und
  // ein Netzfehler dürfte ihn nicht aufhalten.
  const mitschauen = route.viewerToken !== null

  useEffect(() => {
    if (mitschauen) return
    let abgebrochen = false

    api
      .authConfig()
      .then(async (config) => {
        // Steht die Sitzung noch? Das Cookie liest kein Skript, also fragen.
        const ich = config.required ? await api.me().catch(() => null) : null
        if (abgebrochen) return
        setKonto(ich)
        setAuth(config)
      })
      .catch(() => {
        // Antwortet der Server nicht, ist offen, ob eine Anmeldung nötig wäre.
        // Dann lieber fragen als durchlassen.
        if (!abgebrochen) {
          setAuth({ required: true, googleClientId: '' })
          setFehler('Der Server antwortet gerade nicht.')
        }
      })

    return () => {
      abgebrochen = true
    }
  }, [mitschauen])

  // Das Google-Token wird gleich eingelöst. Ein Konto, das nicht freigegeben
  // ist (ADR-0023), bekommt dabei keine Sitzung, sondern einen Satz, warum.
  const anmelden = useCallback((credential: string) => {
    setFehler('')
    api
      .signIn(credential)
      .then(setKonto)
      .catch((e: Error) => setFehler(e.message))
  }, [])

  const abmelden = useCallback(() => {
    void api
      .logout()
      .catch(() => undefined)
      .then(() => setKonto(null))
  }, [])

  if (mitschauen) return <PublicScreen token={route.viewerToken!} />

  if (auth === null) return <Lade />

  if (auth.required && !konto) {
    if (!auth.googleClientId) {
      return <Hinweis text={fehler || 'Für diese Instanz ist eine Anmeldung verlangt, aber keine Google-Client-Id hinterlegt.'} />
    }
    return <SignIn clientId={auth.googleClientId} onToken={anmelden} hinweis={fehler} />
  }

  // Der Eintragen-Link verlangt, wie der Verwalterlink, die Anmeldung der
  // Instanz — er schreibt, anders als das Mitschauen.
  if (route.scorerToken) return <ScorerScreen token={route.scorerToken} />

  return <ChatScreen adminToken={route.adminToken} konto={konto ?? null} onAbmelden={abmelden} />
}

function Lade() {
  return (
    <div className="signin">
      <div className="signin__box">
        <p className="muted">Einen Moment …</p>
      </div>
    </div>
  )
}

function Hinweis({ text }: { text: string }) {
  return (
    <div className="signin">
      <div className="signin__box">
        <p className="signin__error">{text}</p>
      </div>
    </div>
  )
}

function read() {
  const params = new URLSearchParams(window.location.search)
  return { viewerToken: params.get('t'), adminToken: params.get('a'), scorerToken: params.get('s') }
}
