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
import { clearCallbackParams, isAuthConfigured, isRedirectCallback, userManager } from './oidc'
import { setTokenProvider } from '../api/client'

interface AuthState {
  user: User | null
  status: 'loading' | 'anonymous' | 'authenticated'
  /** Ohne konfigurierte Authority gibt es nur die öffentliche Ansicht. */
  configured: boolean
  error: string | null
  login: () => void
  logout: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [status, setStatus] = useState<AuthState['status']>(
    isAuthConfigured ? 'loading' : 'anonymous',
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
          const signedIn = await manager.signinRedirectCallback()
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
      setStatus('anonymous')
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
    void userManager.signinRedirect().catch((cause: unknown) => {
      setError(cause instanceof Error ? cause.message : 'Anmeldung nicht möglich.')
    })
  }, [])

  const logout = useCallback(() => {
    if (!userManager) return
    // Nur lokal abmelden: ein End-Session-Redirect würde am Vereinsrechner auch
    // andere Anwendungen desselben Realms abmelden.
    void userManager.removeUser()
  }, [])

  const value = useMemo<AuthState>(
    () => ({ user, status, configured: isAuthConfigured, error, login, logout }),
    [user, status, error, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth muss innerhalb von <AuthProvider> stehen.')
  return context
}
