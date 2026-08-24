/**
 * Anmeldung und Anmeldemaske.
 *
 * Der `UserManager` ist hier eine Attrappe: was geprüft wird, ist die
 * Verdrahtung — wird der Code genau einmal eingelöst, sieht der API-Client das
 * Token, und was passiert, wenn die Sitzung abläuft, während jemand am Platz
 * steht.
 */

import { render, screen, waitFor } from '@testing-library/react'
import type { User, UserManager } from 'oidc-client-ts'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { user as userEvent } from '../test/render'

const state = {
  configured: true,
  openAccess: false,
  redirectCallback: false,
  existing: null as User | null,
  signedIn: null as User | null,
  completeError: null as Error | null,
  getUserError: null as Error | null,
  /** Hält den Tausch auf, bis der Test ihn freigibt. */
  completeGate: null as Promise<void> | null,
}

const signinRedirect = vi.fn(() => Promise.resolve())
const removeUser = vi.fn(() => Promise.resolve())
const clearCallbackParams = vi.fn()

const events = {
  loaded: [] as ((user: User) => void)[],
  unloaded: [] as (() => void)[],
  expired: [] as (() => void)[],
}

const manager = {
  getUser: vi.fn(() =>
    state.getUserError ? Promise.reject(state.getUserError) : Promise.resolve(state.existing),
  ),
  signinRedirect,
  removeUser,
  events: {
    addUserLoaded: (handler: (user: User) => void) => events.loaded.push(handler),
    removeUserLoaded: (handler: (user: User) => void) => {
      events.loaded = events.loaded.filter((h) => h !== handler)
    },
    addUserUnloaded: (handler: () => void) => events.unloaded.push(handler),
    removeUserUnloaded: (handler: () => void) => {
      events.unloaded = events.unloaded.filter((h) => h !== handler)
    },
    addAccessTokenExpired: (handler: () => void) => events.expired.push(handler),
    removeAccessTokenExpired: (handler: () => void) => {
      events.expired = events.expired.filter((h) => h !== handler)
    },
  },
} as unknown as UserManager

vi.mock('./oidc', () => ({
  get isAuthConfigured() {
    return state.configured
  },
  get isOpenAccess() {
    return state.openAccess
  },
  get userManager() {
    return state.configured ? manager : null
  },
  isRedirectCallback: () => state.redirectCallback,
  completeSignin: async () => {
    if (state.completeGate) await state.completeGate
    if (state.completeError) throw state.completeError
    return state.signedIn!
  },
  clearCallbackParams,
  displayName: (u: User | null) => (u?.profile.name as string | undefined) ?? '',
  initials: (u: User | null) => ((u?.profile.name as string | undefined) ?? '··').slice(0, 2),
}))

const { AuthProvider, useAuth } = await import('./AuthProvider')
const { LoginScreen } = await import('./LoginScreen')
const { setTokenProvider } = await import('../api/client')

function alsBenutzer(over: Partial<User> = {}): User {
  return {
    access_token: 'tok-123',
    expired: false,
    profile: { name: 'S. Moser' },
    ...over,
  } as unknown as User
}

/** Zeigt den Zustand der Anmeldung als Text an. */
function Anzeige() {
  const { status, user, error, configured, login, logout } = useAuth()
  return (
    <div>
      <span data-testid="status">{status}</span>
      <span data-testid="user">{(user?.profile.name as string) ?? '—'}</span>
      <span data-testid="error">{error ?? '—'}</span>
      <span data-testid="configured">{String(configured)}</span>
      <button type="button" onClick={login}>
        anmelden
      </button>
      <button type="button" onClick={logout}>
        abmelden
      </button>
    </div>
  )
}

function aufbau() {
  return render(
    <AuthProvider>
      <Anzeige />
    </AuthProvider>,
  )
}

beforeEach(() => {
  state.configured = true
  state.openAccess = false
  state.redirectCallback = false
  state.existing = null
  state.signedIn = null
  state.completeError = null
  state.getUserError = null
  state.completeGate = null
  events.loaded = []
  events.unloaded = []
  events.expired = []
  signinRedirect.mockClear()
  removeUser.mockClear()
  clearCallbackParams.mockClear()
})

afterEach(() => setTokenProvider(() => null))

describe('AuthProvider', () => {
  it('verlangt, dass er um den Verbraucher steht', () => {
    expect(() => render(<Anzeige />)).toThrow(
      'useAuth muss innerhalb von <AuthProvider> stehen.',
    )
  })

  it('meldet ohne bestehende Sitzung „anonym"', async () => {
    aufbau()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
    expect(screen.getByTestId('user')).toHaveTextContent('—')
  })

  it('übernimmt eine bestehende Sitzung', async () => {
    state.existing = alsBenutzer()
    aufbau()

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    expect(screen.getByTestId('user')).toHaveTextContent('S. Moser')
  })

  it('verwirft eine abgelaufene Sitzung', async () => {
    state.existing = alsBenutzer({ expired: true })
    aufbau()

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
  })

  it('löst den Rücksprung vom IdP ein und räumt die Adresszeile auf', async () => {
    state.redirectCallback = true
    state.signedIn = alsBenutzer()
    aufbau()

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    expect(clearCallbackParams).toHaveBeenCalled()
  })

  it('meldet einen gescheiterten Rücksprung und bleibt anonym', async () => {
    state.redirectCallback = true
    state.completeError = new Error('Code not valid')
    aufbau()

    await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent('Code not valid'))
    expect(screen.getByTestId('status')).toHaveTextContent('anonymous')
    expect(clearCallbackParams).toHaveBeenCalled()
  })

  it('nennt einen unbekannten Fehlschlag beim Namen', async () => {
    state.redirectCallback = true
    // eslint-disable-next-line @typescript-eslint/only-throw-error
    state.completeError = 'kaputt' as unknown as Error
    aufbau()

    await waitFor(() =>
      expect(screen.getByTestId('error')).toHaveTextContent('Anmeldung fehlgeschlagen.'),
    )
  })

  it('bricht ab, wenn der Baum abgebaut wird, bevor die Antwort da ist', async () => {
    let freigeben: (value: User | null) => void = () => {}
    manager.getUser = vi.fn(
      () =>
        new Promise<User | null>((resolve) => {
          freigeben = resolve
        }),
    )

    const { unmount } = aufbau()
    unmount()
    freigeben(alsBenutzer())

    // Kein Zustandswechsel auf einem abgebauten Baum — und kein Fehler.
    await new Promise((resolve) => setTimeout(resolve, 10))
    manager.getUser = vi.fn(() => Promise.resolve(state.existing))
  })

  it('verwirft einen Rücksprung, dessen Baum inzwischen abgebaut ist', async () => {
    state.redirectCallback = true
    state.signedIn = alsBenutzer()

    let freigeben: () => void = () => {}
    state.completeGate = new Promise<void>((resolve) => {
      freigeben = resolve
    })

    const { unmount } = aufbau()
    unmount()
    freigeben()

    await new Promise((resolve) => setTimeout(resolve, 10))
    expect(clearCallbackParams).toHaveBeenCalled()
  })

  it('verschweigt auch einen Fehlschlag, dessen Baum abgebaut ist', async () => {
    state.redirectCallback = true
    state.completeError = new Error('Code not valid')

    let freigeben: () => void = () => {}
    state.completeGate = new Promise<void>((resolve) => {
      freigeben = resolve
    })

    const { unmount } = aufbau()
    unmount()
    freigeben()

    await new Promise((resolve) => setTimeout(resolve, 10))
    expect(screen.queryByTestId('error')).not.toBeInTheDocument()
  })

  it('übernimmt einen später geladenen Benutzer', async () => {
    aufbau()
    await waitFor(() => expect(events.loaded.length).toBeGreaterThan(0))

    events.loaded.forEach((handler) => handler(alsBenutzer({ profile: { name: 'Neu' } } as Partial<User>)))

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    expect(screen.getByTestId('user')).toHaveTextContent('Neu')
  })

  it('fällt zurück auf anonym, wenn die Sitzung endet', async () => {
    state.existing = alsBenutzer()
    aufbau()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

    events.unloaded.forEach((handler) => handler())
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
  })

  it('fällt auch zurück, wenn das Token abläuft', async () => {
    state.existing = alsBenutzer()
    aufbau()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

    events.expired.forEach((handler) => handler())
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
  })

  it('hängt die Empfänger beim Abbau wieder aus', async () => {
    const { unmount } = aufbau()
    await waitFor(() => expect(events.loaded.length).toBeGreaterThan(0))

    unmount()

    expect(events.loaded).toHaveLength(0)
    expect(events.unloaded).toHaveLength(0)
    expect(events.expired).toHaveLength(0)
  })

  it('führt zum IdP', async () => {
    aufbau()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))

    await userEvent().click(screen.getByRole('button', { name: 'anmelden' }))
    expect(signinRedirect).toHaveBeenCalled()
  })

  it('meldet, wenn der Weg zum IdP scheitert', async () => {
    signinRedirect.mockRejectedValueOnce(new Error('IdP nicht erreichbar'))
    aufbau()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))

    await userEvent().click(screen.getByRole('button', { name: 'anmelden' }))
    await waitFor(() =>
      expect(screen.getByTestId('error')).toHaveTextContent('IdP nicht erreichbar'),
    )
  })

  it('nennt auch hier einen unbekannten Fehlschlag beim Namen', async () => {
    signinRedirect.mockRejectedValueOnce('kaputt' as unknown as Error)
    aufbau()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))

    await userEvent().click(screen.getByRole('button', { name: 'anmelden' }))
    await waitFor(() =>
      expect(screen.getByTestId('error')).toHaveTextContent('Anmeldung nicht möglich.'),
    )
  })

  it('meldet nur lokal ab — nicht am IdP', async () => {
    state.existing = alsBenutzer()
    aufbau()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

    await userEvent().click(screen.getByRole('button', { name: 'abmelden' }))
    expect(removeUser).toHaveBeenCalled()
  })

  it('tut ohne konfigurierte Authority gar nichts', async () => {
    state.configured = false
    aufbau()

    await waitFor(() => expect(screen.getByTestId('configured')).toHaveTextContent('false'))
    expect(screen.getByTestId('status')).toHaveTextContent('anonymous')

    await userEvent().click(screen.getByRole('button', { name: 'anmelden' }))
    await userEvent().click(screen.getByRole('button', { name: 'abmelden' }))

    expect(signinRedirect).not.toHaveBeenCalled()
    expect(removeUser).not.toHaveBeenCalled()
  })

  it('gibt dem API-Client das jeweils aktuelle Token', async () => {
    state.existing = alsBenutzer()
    let gesehen: string | null = null

    const { setTokenProvider: set } = await import('../api/client')
    const original = set
    void original

    aufbau()
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

    // Der Provider liegt jetzt im Client; ein Aufruf holt ihn über eine Anfrage.
    const { http } = await import('../api/client')
    const { server } = await import('../test/server')
    const { http: msw, HttpResponse } = await import('msw')

    server.use(
      msw.get('/api/probe', ({ request }) => {
        gesehen = request.headers.get('Authorization')
        return HttpResponse.json({})
      }),
    )

    await http.get('/api/probe')
    expect(gesehen).toBe('Bearer tok-123')
  })
})

describe('LoginScreen', () => {
  function maske() {
    const onPublicView = vi.fn()
    render(
      <AuthProvider>
        <LoginScreen onPublicView={onPublicView} />
      </AuthProvider>,
    )
    return onPublicView
  }

  it('nennt die Anmeldung und wer die Rollen vergibt', async () => {
    maske()
    expect(screen.getByText('Turnierleitung')).toBeInTheDocument()
    expect(
      screen.getByText(/Die Rollen vergibt die Anwendung, nicht der IdP/),
    ).toBeInTheDocument()
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Anmelden' })).not.toBeDisabled(),
    )
  })

  it('stellt die öffentliche Ansicht daneben — sie braucht kein Konto', async () => {
    const onPublicView = maske()

    await userEvent().click(screen.getByRole('button', { name: 'Öffentliche Live-Ansicht' }))
    expect(onPublicView).toHaveBeenCalled()
  })

  it('führt zum IdP', async () => {
    maske()
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Anmelden' })).not.toBeDisabled(),
    )

    await userEvent().click(screen.getByRole('button', { name: 'Anmelden' }))
    expect(signinRedirect).toHaveBeenCalled()
  })

  it('sagt ohne Authority, welche Variable fehlt', async () => {
    state.configured = false
    maske()

    // Die Server-Variable zuerst: eine ausgelieferte Instanz bekommt ihre
    // Anmeldedaten über /config.js, und wer dort VITE_OIDC_AUTHORITY sucht,
    // sucht an einem Ort, den es im Bild gar nicht mehr gibt.
    expect(screen.getByText('Oidc__Authority')).toBeInTheDocument()
    expect(screen.getByText('VITE_OIDC_AUTHORITY')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Anmelden' })).toBeDisabled()
  })

  it('sperrt den Knopf, solange die Anmeldung geprüft wird', () => {
    maske()
    expect(screen.getByRole('button', { name: 'Anmelden' })).toBeDisabled()
  })

  it('zeigt einen Fehlschlag an', async () => {
    state.redirectCallback = true
    state.completeError = new Error('Code not valid')
    maske()

    expect(await screen.findByText('Code not valid')).toBeInTheDocument()
  })
})
