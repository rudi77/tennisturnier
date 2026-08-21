/**
 * Die Anwendung als Ganzes.
 *
 * Geprüft wird die Weiche am Eingang — sie ist die Stelle, an der eine
 * Änderung am meisten kaputtmacht: der Anmeldelink steht vor der Anmeldemaske,
 * ein geteilter Zuschauerlink führt zum Zusehen und nicht zum Anmelden, und
 * ein fremdes Turnier bleibt ohne Arbeitsbereich.
 *
 * Der Identity Provider ist eine Attrappe; er hat seinen eigenen Test.
 */

import { render, screen, waitFor } from '@testing-library/react'
import type { User, UserManager } from 'oidc-client-ts'
import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as fx from './test/fixtures'
import { db, server } from './test/server'
import { user as userEvent } from './test/render'

const state = {
  configured: true,
  existing: null as User | null,
}

const signinRedirect = vi.fn(() => Promise.resolve())
const removeUser = vi.fn(() => Promise.resolve())

const manager = {
  getUser: () => Promise.resolve(state.existing),
  signinRedirect,
  removeUser,
  events: {
    addUserLoaded: () => {},
    removeUserLoaded: () => {},
    addUserUnloaded: () => {},
    removeUserUnloaded: () => {},
    addAccessTokenExpired: () => {},
    removeAccessTokenExpired: () => {},
  },
} as unknown as UserManager

vi.mock('./auth/oidc', () => ({
  get isAuthConfigured() {
    return state.configured
  },
  get userManager() {
    return state.configured ? manager : null
  },
  isRedirectCallback: () => false,
  completeSignin: () => Promise.resolve(state.existing!),
  clearCallbackParams: () => {},
  displayName: (u: User | null) => (u?.profile.name as string | undefined) ?? '',
  initials: () => 'SM',
}))

vi.mock('./api/realtime', () => ({
  PROJECTION_CHANGED: 'projectionChanged',
  subscribeToTournament: () => () => {},
}))

const { App } = await import('./App')

const T = fx.IDS.tournament

function angemeldet(): User {
  return {
    access_token: 'tok-123',
    expired: false,
    profile: { name: 'S. Moser' },
  } as unknown as User
}

beforeEach(() => {
  state.configured = true
  state.existing = null
  signinRedirect.mockClear()
  removeUser.mockClear()
})

function bei(pfad: string): void {
  window.history.replaceState({}, '', pfad)
}

/**
 * Ein Punkt der Hauptnavigation, über seine Nummer.
 *
 * Über den Text ginge es nicht: jeder Punkt trägt seine Beschriftung zweimal
 * — einmal lang für die Seitenleiste, einmal kurz für die Fußleiste.
 */
function navPunkt(nummer: string): HTMLElement {
  return screen.getByRole('button', { name: new RegExp(`^${nummer}`) })
}

describe('App — Eingang', () => {
  it('prüft zuerst die Anmeldung', () => {
    render(<App />)
    expect(screen.getByRole('status')).toHaveTextContent('Anmeldung wird geprüft …')
  })

  it('stellt den Anmeldelink vor die Anmeldemaske', async () => {
    bei('/?r=tok-abcdef')
    render(<App />)

    // Ohne Ladeanzeige und ohne Maske: wer über einen Aushang kommt, soll kein
    // Konto brauchen.
    expect(await screen.findByText('Clubmeisterschaft 2026')).toBeInTheDocument()
    expect(screen.queryByText('Turnierleitung')).not.toBeInTheDocument()
  })

  it('zeigt ohne Anmeldung die Maske', async () => {
    render(<App />)

    expect(await screen.findByText('Turnierleitung')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Anmelden' })).toBeInTheDocument()
  })

  it('führt von der Maske in die öffentliche Ansicht', async () => {
    render(<App />)
    await screen.findByText('Turnierleitung')

    await userEvent().click(screen.getByRole('button', { name: 'Öffentliche Live-Ansicht' }))

    expect(await screen.findByText('Live-Ansicht')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Anmelden' })).toBeInTheDocument()
  })

  it('führt einen geteilten Zuschauerlink direkt zum Zusehen', async () => {
    bei(`/?t=${T}`)
    render(<App />)

    expect(await screen.findByText('Clubmeisterschaft 2026')).toBeInTheDocument()
    expect(screen.queryByText('Turnierleitung')).not.toBeInTheDocument()
  })

  it('führt von dort auf Wunsch zum Identity Provider', async () => {
    bei(`/?t=${T}`)
    render(<App />)

    await userEvent().click(await screen.findByRole('button', { name: 'Anmelden' }))
    expect(signinRedirect).toHaveBeenCalled()
  })

  it('läuft ohne konfigurierte Authority rein öffentlich', async () => {
    state.configured = false
    bei(`/?t=${T}`)
    render(<App />)

    expect(await screen.findByText('Clubmeisterschaft 2026')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Anmelden' })).not.toBeInTheDocument()
  })
})

describe('App — Arbeitsbereich', () => {
  beforeEach(() => {
    state.existing = angemeldet()
  })

  it('steigt im Ablauf ein', async () => {
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Clubmeisterschaft 2026' })).toBeInTheDocument()
    expect(screen.getAllByRole('button').some((b) => b.getAttribute('aria-current') === 'page')).toBe(
      true,
    )
  })

  it('wählt das erste eigene Turnier, wo keines in der Adresse steht', async () => {
    render(<App />)

    await waitFor(() => expect(window.location.search).toBe(`?t=${T}`))
  })

  it('navigiert zwischen den Bildschirmen', async () => {
    render(<App />)
    await screen.findByRole('heading', { name: 'Clubmeisterschaft 2026' })
    const u = userEvent()

    for (const [nummer, ueberschrift] of [
      ['02', 'Meine Turniere'],
      ['03', 'Meldungen'],
      ['04', 'Draw & Bracket'],
      ['05', 'Spielplan'],
      ['06', 'Turnier anlegen'],
      ['07', 'Live-Ansicht'],
    ] as const) {
      await u.click(navPunkt(nummer))
      expect(await screen.findByRole('heading', { name: ueberschrift })).toBeInTheDocument()
    }
  })

  it('fällt bei einem unbekannten Bildschirm auf den Ablauf zurück', async () => {
    bei(`/?screen=unfug&t=${T}`)
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Clubmeisterschaft 2026' })).toBeInTheDocument()
  })

  it('geht aus der Turnierliste in den Ablauf', async () => {
    bei(`/?screen=tournaments&t=${T}`)
    render(<App />)

    await userEvent().click(await screen.findByText('Clubmeisterschaft 2026'))

    expect(await screen.findByRole('heading', { name: 'Clubmeisterschaft 2026' })).toBeInTheDocument()
  })

  it('führt aus der Turnierliste zum Anlegen', async () => {
    bei(`/?screen=tournaments&t=${T}`)
    render(<App />)

    await userEvent().click(await screen.findByRole('button', { name: 'Turnier anlegen' }))

    expect(await screen.findByRole('heading', { name: 'Turnier anlegen' })).toBeInTheDocument()
  })

  it('wechselt nach dem Anlegen in den Ablauf', async () => {
    bei(`/?screen=create&t=${T}`)
    render(<App />)

    const u = userEvent()
    await u.type(await screen.findByLabelText('Name'), 'Neu')
    await u.type(screen.getByLabelText('Anlage'), 'TC Neu')
    await u.click(screen.getByRole('button', { name: /05\s*Zusammenfassung/ }))
    await u.click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() => expect(window.location.search).toContain('screen=flow'))
  })

  it('kommt ohne eigenes Turnier zurecht', async () => {
    db.tournaments = []
    bei('/')
    render(<App />)

    expect(await screen.findByText('Noch kein Turnier')).toBeInTheDocument()
    expect(screen.getByText('kein Turnier geladen')).toBeInTheDocument()
  })

  it('meldet einen Fehler an der Turnierliste', async () => {
    server.use(http.get('/api/tournaments', () => new HttpResponse(null, { status: 503 })))
    render(<App />)

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await userEvent().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
  })

  it('meldet ab', async () => {
    render(<App />)
    await screen.findByRole('heading', { name: 'Clubmeisterschaft 2026' })

    await userEvent().click(screen.getByRole('button', { name: 'Abmelden' }))
    expect(removeUser).toHaveBeenCalled()
  })
})

describe('App — fremdes Turnier', () => {
  beforeEach(() => {
    state.existing = angemeldet()
    // Der Link zeigt auf ein Turnier, das dem Angemeldeten nicht gehört.
    db.tournaments = [fx.tournamentSummary({ id: 'eigenes-1', name: 'Mein Turnier' })]
    db.tournament = fx.tournamentDetail({ id: fx.IDS.otherTournament })
    db.publicView = fx.publicView({ id: fx.IDS.otherTournament, name: 'Fremdes Turnier' })
  })

  it('zeigt es ohne Arbeitsbereich — dort gäbe es nichts zu tun', async () => {
    bei(`/?t=${fx.IDS.otherTournament}`)
    render(<App />)

    expect(await screen.findByText('Fremdes Turnier')).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Hauptnavigation' })).not.toBeInTheDocument()
  })

  it('führt zurück zu den eigenen Turnieren', async () => {
    bei(`/?t=${fx.IDS.otherTournament}`)
    render(<App />)

    await userEvent().click(await screen.findByRole('button', { name: 'Meine Turniere' }))

    await waitFor(() =>
      expect(screen.getByRole('navigation', { name: 'Hauptnavigation' })).toBeInTheDocument(),
    )
  })
})
