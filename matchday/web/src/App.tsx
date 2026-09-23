import { useCallback, useEffect, useState } from 'react'
import { ChatScreen } from './ChatScreen'
import { PublicScreen } from './PublicScreen'
import { ScorerScreen } from './ScorerScreen'
import { SignIn } from './SignIn'
import { api } from './api'
import { ABGEMELDET, abgelaufen, idToken, kontoAus, rememberToken, type AuthConfig } from './auth'

/**
 * Drei Adressen, kein Router: `?t=<id>` ist der Mitschau-Link für alle,
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
  const [token, setToken] = useState<string | null>(() => gültigesToken())
  const [fehler, setFehler] = useState('')

  useEffect(() => {
    const onPop = () => setRoute(read())
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  // Läuft das Token mitten im Gespräch ab, meldet die API das einmal — und
  // hier landet man wieder auf der Anmeldung statt in einer Reihe von 401ern.
  useEffect(() => {
    const abmelden = () => setToken(null)
    window.addEventListener(ABGEMELDET, abmelden)
    return () => window.removeEventListener(ABGEMELDET, abmelden)
  }, [])

  // Der Mitschau-Weg fragt gar nicht erst: Er braucht die Antwort nicht, und
  // ein Netzfehler dürfte ihn nicht aufhalten.
  const mitschauen = route.publicId !== null

  useEffect(() => {
    if (mitschauen) return
    let abgebrochen = false

    api
      .authConfig()
      .then((config) => {
        if (!abgebrochen) setAuth(config)
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

  const anmelden = useCallback((neues: string) => {
    rememberToken(neues)
    setToken(neues)
  }, [])

  if (mitschauen) return <PublicScreen tournamentId={route.publicId!} />

  if (auth === null) return <Lade />

  if (auth.required && token === null) {
    if (!auth.googleClientId) {
      return <Hinweis text={fehler || 'Für diese Instanz ist eine Anmeldung verlangt, aber keine Google-Client-Id hinterlegt.'} />
    }
    return <SignIn clientId={auth.googleClientId} onToken={anmelden} />
  }

  // Der Eintragen-Link verlangt, wie der Verwalterlink, die Anmeldung der
  // Instanz — er schreibt, anders als das Mitschauen.
  if (route.scorerToken) return <ScorerScreen token={route.scorerToken} />

  return <ChatScreen adminToken={route.adminToken} />
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

/** Ein abgelaufenes Token ist so gut wie keins — es gäbe nur 401er. */
function gültigesToken(): string | null {
  const vorhanden = idToken()
  if (!vorhanden) return null

  const konto = kontoAus(vorhanden)
  if (konto === null || abgelaufen(konto)) {
    rememberToken(null)
    return null
  }

  return vorhanden
}

function read() {
  const params = new URLSearchParams(window.location.search)
  return { publicId: params.get('t'), adminToken: params.get('a'), scorerToken: params.get('s') }
}
