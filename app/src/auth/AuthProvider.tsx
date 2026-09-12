import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import type { User } from 'oidc-client-ts'
import {
  clearCallbackParams,
  completeSignin,
  isAuthConfigured,
  isOpenAccess,
  isRedirectCallback,
  stashRoute,
  userManager,
} from './oidc'
import { setTokenProvider } from '../api/client'

interface AuthState {
  user: User | null
  /**
   * `signing-out` ist ein eigener Zustand und nicht bloß „noch angemeldet":
   * `signoutRedirect` räumt die Sitzung hier weg, *bevor* es zum Aussteller
   * weiterleitet. Ohne diesen Zwischenzustand stünde die Anwendung in genau
   * diesem Fenster als `anonymous` da — und `ToLogin` schickte den Abmeldenden
   * postwendend wieder zur Anmeldung, mitten in die laufende Abmeldung hinein.
   */
  status: 'loading' | 'anonymous' | 'authenticated' | 'signing-out'
  /** Ohne konfigurierte Authority gibt es nur die öffentliche Ansicht. */
  configured: boolean
  /**
   * Diese Instanz läuft ohne Anmeldung: der Server lässt jeden Aufruf zu und
   * behandelt ihn als denselben Benutzer. Dann gibt es nichts anzumelden — und
   * die Arbeitsoberfläche steht trotzdem offen.
   */
  openAccess: boolean
  error: string | null
  login: () => void
  logout: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  // Ohne Anmeldung gibt es nichts zu prüfen und nichts zu warten: der Server
  // hat bereits entschieden, dass jeder Aufruf durchgeht.
  const [status, setStatus] = useState<AuthState['status']>(
    isAuthConfigured ? 'loading' : isOpenAccess ? 'authenticated' : 'anonymous',
  )
  const [error, setError] = useState<string | null>(null)

  // Der Token-Zugriff geht über eine Ref, damit der API-Client immer den
  // aktuellen Wert sieht, ohne dass ein erneuertes Token jede Komponente neu
  // rendert.
  const tokenRef = useRef<string | null>(null)
  tokenRef.current = user?.access_token ?? null

  useEffect(() => {
    setTokenProvider(() => tokenRef.current)
  }, [])

  useEffect(() => {
    const manager = userManager
    if (!manager) return

    let cancelled = false

    const load = async () => {
      try {
        if (isRedirectCallback()) {
          // Der Tausch läuft über completeSignin und nicht direkt über den
          // Manager: der zweite Lauf unter <StrictMode> bekommt so dieselbe
          // Zusage statt eines zweiten Einlöseversuchs.
          const signedIn = await completeSignin(manager)
          clearCallbackParams()
          if (cancelled) return
          setUser(signedIn)
          setStatus('authenticated')
          return
        }

        const existing = await manager.getUser()
        if (cancelled) return

        if (existing && !existing.expired) {
          setUser(existing)
          setStatus('authenticated')
        } else {
          setStatus('anonymous')
        }
      } catch (cause) {
        if (cancelled) return
        clearCallbackParams()
        setError(cause instanceof Error ? cause.message : 'Anmeldung fehlgeschlagen.')
        setStatus('anonymous')
      }
    }

    void load()

    const onLoaded = (next: User) => {
      setUser(next)
      setStatus('authenticated')
    }
    const onUnloaded = () => {
      setUser(null)
      // Nicht während einer laufenden Abmeldung: `signoutRedirect` löst dieses
      // Ereignis selbst aus, kurz bevor es weiterleitet. Ein `anonymous` an
      // dieser Stelle wäre das Startsignal für die Weiterleitung zur Anmeldung
      // — und die käme der Abmeldung zuvor.
      setStatus((vorher) => (vorher === 'signing-out' ? vorher : 'anonymous'))
    }

    manager.events.addUserLoaded(onLoaded)
    manager.events.addUserUnloaded(onUnloaded)
    manager.events.addAccessTokenExpired(onUnloaded)

    return () => {
      cancelled = true
      manager.events.removeUserLoaded(onLoaded)
      manager.events.removeUserUnloaded(onUnloaded)
      manager.events.removeAccessTokenExpired(onUnloaded)
    }
  }, [])

  const login = useCallback(() => {
    if (!userManager) return
    setError(null)

    // Wohin er wollte, bevor er weggeschickt wird: die Rücksprungadresse beim
    // Aussteller ist die nackte Wurzel, und ohne diese Zeile landete jeder
    // Beitrittslink nach der Anmeldung im Ablauf irgendeines Turniers.
    stashRoute()

    void userManager.signinRedirect().catch((cause: unknown) => {
      setError(cause instanceof Error ? cause.message : 'Anmeldung nicht möglich.')
    })
  }, [])

  const logout = useCallback(() => {
    if (!userManager) return

    // Beim Aussteller abmelden und nicht bloß hier. Hier stand einmal das
    // Gegenteil, mit der Begründung, ein Rücksprung meldete am Vereinsrechner
    // auch andere Anwendungen desselben Realms ab. Mit persönlichen Konten ist
    // die überlebende Sitzung der Fehler: „Abmelden" hieß, dass der nächste
    // Klick auf „Anmelden" wortlos denselben Menschen zurückbrachte.

    // Ab hier ist die Anwendung weder angemeldet noch abgemeldet, sondern
    // unterwegs. Der Zustand hält `ToLogin` zurück, bis der Browser wirklich
    // beim Aussteller ist; er endet mit dem Verlassen der Seite.
    setStatus('signing-out')

    void userManager.signoutRedirect().catch((cause: unknown) => {
      setError(cause instanceof Error ? cause.message : 'Abmelden nicht möglich.')
      // Der Weg zum Aussteller ist nicht zustande gekommen; die Sitzung hier
      // hat `signoutRedirect` trotzdem schon geräumt. Also zurück in den
      // Zustand, der dazu passt — mit gesetztem Fehler leitet `ToLogin` nicht
      // weiter, sondern sagt, was schiefging.
      setStatus('anonymous')
    })
  }, [])

  const value = useMemo<AuthState>(
    () => ({
      user,
      status,
      configured: isAuthConfigured,
      openAccess: isOpenAccess,
      error,
      login,
      logout,
    }),
    [user, status, error, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth muss innerhalb von <AuthProvider> stehen.')
  return context
}
