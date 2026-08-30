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
  openAccess: false,
  existing: null as User | null,
}

const signinRedirect = vi.fn(() => Promise.resolve())
const signoutRedirect = vi.fn(() => Promise.resolve())
const removeUser = vi.fn(() => Promise.resolve())

const manager = {
  getUser: () => Promise.resolve(state.existing),
  signinRedirect,
  signoutRedirect,
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
  get isOpenAccess() {
    return state.openAccess
  },
  get userManager() {
    return state.configured ? manager : null
  },
  isRedirectCallback: () => false,
  completeSignin: () => Promise.resolve(state.existing!),
  clearCallbackParams: () => {},
  stashRoute: () => {},
  displayName: (u: User | null) => (u?.profile.name as string | undefined) ?? '',
  initials: () => 'SM',
}))

vi.mock('./api/realtime', () => ({
  PROJECTION_CHANGED: 'projectionChanged',
  FEED_CHANGED: 'feedChanged',
  subscribeToTournament: () => () => {},
  subscribeToFeed: () => () => {},
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
  state.openAccess = false
  state.existing = null
  signinRedirect.mockClear()
  signoutRedirect.mockClear()
  removeUser.mockClear()
})

function bei(pfad: string): void {
  window.history.replaceState({}, '', pfad)
}

/**
 * Ein Punkt der Hauptnavigation.
 *
 * Über die lange Beschriftung: die kurze, die in der Fußleiste steht, ist für
 * Hilfsmittel verborgen — sichtbar ist immer nur eine von beiden.
 */
function navPunkt(name: string): HTMLElement {
  return screen.getAllByRole('button', { name }).find((knopf) =>
    knopf.classList.contains('md-nav__item'),
  )!
}

describe('App — Eingang', () => {
  it('prüft zuerst die Anmeldung', () => {
    render(<App />)
    expect(screen.getByRole('status')).toHaveTextContent('Anmeldung wird geprüft …')
  })

  it('führt einen Beitrittslink durch die Anmeldung', async () => {
    // Der Kern des Umbaus: der Link führt jetzt durch die Anmeldung statt an
    // ihr vorbei — beitreten kann nur, wer ein Konto hat.
    bei('/?r=tok-abcdef')
    render(<App />)

    expect(await screen.findByText('Weiterleitung zur Anmeldung …')).toBeInTheDocument()
    await waitFor(() => expect(signinRedirect).toHaveBeenCalled())
  })

  it('zeigt den Beitritt, sobald jemand angemeldet ist', async () => {
    state.existing = angemeldet()
    bei('/?r=tok-abcdef')
    render(<App />)

    expect(await screen.findByText('Clubmeisterschaft 2026')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Melden und beitreten/ })).toBeInTheDocument()
  })

  it('führt nach dem Beitritt in den Ablauf des Turniers', async () => {
    state.existing = angemeldet()
    bei('/?r=tok-abcdef')
    render(<App />)

    // Ohne zu melden: der Name steht in diesem Konto nicht getrennt, und für
    // den Weg in den Ablauf macht es keinen Unterschied.
    const u = userEvent()
    await u.click(
      await screen.findByRole('button', { name: 'Nur beitreten, ohne mitzuspielen' }),
    )
    await u.click(await screen.findByRole('button', { name: 'Turnier öffnen' }))

    await waitFor(() => expect(window.location.search).toContain('screen=flow'))
    expect(window.location.search).toContain(`t=${T}`)
    expect(window.location.search).not.toContain('r=')
  })

  it('schickt ohne Anmeldung sofort zum Aussteller', async () => {
    // Hier stand eine Startseite mit zwei Schaltflächen. Die eine war ein
    // Zwischenschritt, der nichts fragte; die andere führte ohne Turnier auf
    // eine leere Seite.
    render(<App />)

    expect(await screen.findByText('Weiterleitung zur Anmeldung …')).toBeInTheDocument()
    await waitFor(() => expect(signinRedirect).toHaveBeenCalledTimes(1))
  })

  it('leitet nach einem Fehlschlag nicht weiter, sondern erklärt ihn', async () => {
    // Sonst entstünde eine Schleife, in der die Anwendung bei jedem Fehlschlag
    // erneut wegschickt und niemand erfährt, warum.
    signinRedirect.mockRejectedValueOnce(new Error('IdP nicht erreichbar'))
    render(<App />)

    expect(await screen.findByText('IdP nicht erreichbar')).toBeInTheDocument()

    await userEvent().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
    expect(signinRedirect).toHaveBeenCalledTimes(2)
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

  it('zeigt ohne Aussteller und ohne Turnier die Zuschauerseite', async () => {
    // Es gibt nichts, wohin man leiten könnte. Die Zuschauerseite ist der
    // einzige Teil, der ohne Anmeldung überhaupt etwas zeigen kann.
    state.configured = false
    bei('/')
    render(<App />)

    expect(await screen.findByText(/Die Adresse braucht die Turnier-Id/)).toBeInTheDocument()
  })

  it('geht im offenen Betrieb ohne Maske in den Arbeitsbereich', async () => {
    // Der erste Schritt einer Instanz: der Server lässt jeden Aufruf zu. Eine
    // Anmeldemaske davor wäre eine Tür ohne Schloss und ohne Schlüssel.
    state.configured = false
    state.openAccess = true
    bei('/')
    render(<App />)

    expect(await screen.findByRole('navigation', { name: 'Hauptnavigation' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Anmelden' })).not.toBeInTheDocument()
  })

  it('sagt im offenen Betrieb, woran man ist', async () => {
    // Sichtbar und nicht nur im Protokoll des Servers: wer hier arbeitet, soll
    // es wissen, bevor er das erste Ergebnis einträgt.
    state.configured = false
    state.openAccess = true
    bei('/')
    render(<App />)

    expect(
      await screen.findByText(/Jeder, der die Adresse kennt, kann hier alles ändern/),
    ).toBeInTheDocument()

    // Und kein Knopf, der eine Sitzung beendet, die es nicht gibt.
    expect(screen.queryByRole('button', { name: 'Abmelden' })).not.toBeInTheDocument()
    expect(screen.getByText('Ohne Anmeldung')).toBeInTheDocument()
  })
})

describe('App — Arbeitsbereich', () => {
  beforeEach(() => {
    state.existing = angemeldet()
  })

  it('steigt im Ablauf ein', async () => {
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Ablauf' })).toBeInTheDocument()
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
    await screen.findByRole('heading', { name: 'Ablauf' })
    const u = userEvent()

    for (const [punkt, ueberschrift] of [
      ['Meine Turniere', 'Meine Turniere'],
      ['Meldungen', 'Meldungen'],
      ['Draw & Bracket', 'Draw & Bracket'],
      ['Spielplan', 'Spielplan'],
      ['Neues Turnier', 'Turnier anlegen'],
      ['Live-Ansicht', 'Live-Ansicht'],
      // Die vier, die nicht zum Turnierablauf gehören (ADR-0013 bis 0015).
      ['Feed', 'Feed'],
      ['Mein Profil', 'Moser, Sabine'],
      ['Mitspieler', 'Mitspieler'],
      ['Verabredungen', 'Verabredungen'],
    ] as const) {
      await u.click(navPunkt(punkt))
      expect(await screen.findByRole('heading', { name: ueberschrift })).toBeInTheDocument()
    }
  })

  /**
   * Aus dem Profil zurück in ein Turnier: die Auswahl wechselt, der Ablauf ist
   * das Ziel, und das Profil wird dabei abgewählt — sonst stünde es beim
   * nächsten Wechsel zurück auf „Profil" noch da.
   */
  it('führt aus dem Profil in das Turnier, das dort steht', async () => {
    bei(`/?screen=profile&t=${T}`)
    render(<App />)

    await userEvent().click(
      await screen.findByRole('button', { name: 'Clubmeisterschaft 2026' }),
    )

    expect(await screen.findByRole('heading', { name: 'Ablauf' })).toBeInTheDocument()
    expect(window.location.search).not.toContain('p=')
  })

  it('fällt bei einem unbekannten Bildschirm auf den Ablauf zurück', async () => {
    bei(`/?screen=unfug&t=${T}`)
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Ablauf' })).toBeInTheDocument()
  })

  it('geht aus der Turnierliste in den Ablauf', async () => {
    bei(`/?screen=tournaments&t=${T}`)
    render(<App />)

    // Die Karte selbst ist die Schaltfläche — ihr Name trägt Ort und Termin
    // mit, und in der Kopfleiste steht derselbe Turniername noch einmal.
    await userEvent().click(await screen.findByRole('button', { name: /Clubmeisterschaft 2026/ }))

    expect(await screen.findByRole('heading', { name: 'Ablauf' })).toBeInTheDocument()
  })

  it('führt aus der Turnierliste zum Anlegen', async () => {
    bei(`/?screen=tournaments&t=${T}`)
    render(<App />)

    await userEvent().click(await screen.findByRole('button', { name: 'Turnier anlegen' }))

    expect(await screen.findByRole('heading', { name: 'Turnier anlegen' })).toBeInTheDocument()
  })

  it('wechselt nach dem Anlegen in den Ablauf', async () => {
    // Zwei Felder und ein Knopf — kein Weg durch fünf Schritte mehr.
    bei(`/?screen=create&t=${T}`)
    render(<App />)

    const u = userEvent()
    await u.type(await screen.findByLabelText('Name'), 'Neu')
    await u.type(screen.getByLabelText('Anlage'), 'TC Neu')
    await u.click(screen.getByRole('button', { name: 'Turnier anlegen' }))

    await waitFor(() => expect(window.location.search).toContain('screen=flow'))
  })

  it('kommt ohne eigenes Turnier zurecht', async () => {
    db.tournaments = []
    bei('/')
    render(<App />)

    expect(await screen.findByText('Noch kein Turnier')).toBeInTheDocument()
    // Und die Kopfleiste sagt es auch: es ist keines gewählt.
    expect(screen.getByText('Kein Turnier')).toBeInTheDocument()
  })

  it('meldet einen Fehler an der Turnierliste', async () => {
    server.use(http.get('/api/tournaments', () => new HttpResponse(null, { status: 503 })))
    render(<App />)

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await userEvent().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
  })

  it('meldet ab', async () => {
    render(<App />)
    await screen.findByRole('heading', { name: 'Ablauf' })

    await userEvent().click(screen.getByRole('button', { name: 'Abmelden' }))
    expect(signoutRedirect).toHaveBeenCalled()
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

    await screen.findByText('Fremdes Turnier')
    await userEvent().click(screen.getByRole('button', { name: 'Meine Turniere' }))

    await waitFor(() =>
      expect(screen.getByRole('navigation', { name: 'Hauptnavigation' })).toBeInTheDocument(),
    )
  })
})
