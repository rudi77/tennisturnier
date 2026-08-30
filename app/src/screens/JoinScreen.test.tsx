/**
 * Der Beitritt über den geteilten Link.
 *
 * Er ersetzt die anonyme Meldung: der Link bleibt die Eintrittskarte, aber wer
 * hindurchgeht, hat ein Konto und gehört danach dazu (ADR-0012). Geprüft wird
 * beides — dass man mitspielen kann und dass man auch bloß dazugehören kann.
 */

import { screen, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { User } from 'oidc-client-ts'
import * as fx from '../test/fixtures'
import { db, lastBody, server } from '../test/server'
import { renderWithProviders, user } from '../test/render'
import { Toast } from '../components/layout/Toast'

const angemeldet = {
  profile: { name: 'S. Moser', given_name: 'Sabine', family_name: 'Moser' },
} as unknown as User

vi.mock('../auth/AuthProvider', async (original) => ({
  ...(await original<typeof import('../auth/AuthProvider')>()),
  useAuth: () => ({
    user: angemeldet,
    status: 'authenticated' as const,
    configured: true,
    openAccess: false,
    error: null,
    login: () => {},
    logout: () => {},
  }),
}))

const { JoinScreen } = await import('./JoinScreen')

function aufbau(token = 'tok-abcdef') {
  const onJoined = vi.fn()

  renderWithProviders(
    <>
      <JoinScreen token={token} onJoined={onJoined} />
      <Toast />
    </>,
    { workspace: null },
  )

  return onJoined
}

describe('JoinScreen — der Kopf', () => {
  it('nennt Turnier, Ort, Termin und Disziplin', async () => {
    aufbau()

    expect(await screen.findByText('Clubmeisterschaft 2026')).toBeInTheDocument()
    expect(screen.getByText(/TC Musterstadt · Musterstadt/)).toBeInTheDocument()
    expect(screen.getByText(/Einzel/)).toBeInTheDocument()
  })

  it('nennt keine Namen — der Link ist kein Weg an der Projektion vorbei', async () => {
    aufbau()
    await screen.findByText('Clubmeisterschaft 2026')

    expect(screen.queryByText(/S\. Moser/)).not.toBeInTheDocument()
  })

  it('lässt den Ort weg, wo keiner steht', async () => {
    db.join = fx.joinView({ city: null })
    aufbau()

    expect(await screen.findByText('TC Musterstadt')).toBeInTheDocument()
  })

  it('sagt bei einem unbekannten Token, dass der Link nirgendwohin führt', async () => {
    // Ein unbekanntes Token und ein Turnier, über das gerade nichts geht, sind
    // von außen nicht zu unterscheiden — der Text sagt deshalb beides.
    aufbau('unbekannt')

    expect(await screen.findByText('Dieser Link führt nirgendwohin')).toBeInTheDocument()
  })

  it('meldet einen echten Fehler als solchen', async () => {
    server.use(http.get('/api/join/:token', () => new HttpResponse(null, { status: 503 })))
    aufbau()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
  })

  it('sagt beim vollen Feld, dass die Meldung auf die Warteliste geht', async () => {
    db.join = fx.joinView({ freeSlots: 0 })
    aufbau()

    expect(await screen.findByText(/Das Feld ist voll/)).toBeInTheDocument()
  })
})

describe('JoinScreen — mitspielen', () => {
  it('füllt den Namen aus dem Konto vor', async () => {
    aufbau()

    expect(await screen.findByLabelText('Vorname')).toHaveValue('Sabine')
    expect(screen.getByLabelText('Nachname')).toHaveValue('Moser')
  })

  it('meldet und tritt in einem Zug bei', async () => {
    const onJoined = aufbau()
    await screen.findByLabelText('Vorname')

    await user().click(screen.getByRole('button', { name: 'Melden und beitreten' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/join/tok-abcdef')).toMatchObject({
        play: true,
        firstName: 'Sabine',
        lastName: 'Moser',
        partnerFirstName: null,
      }),
    )

    expect(await screen.findByText('Du bist dabei')).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Turnier öffnen' }))
    expect(onJoined).toHaveBeenCalledWith(fx.IDS.tournament)
  })

  it('schickt Telefonnummer und geänderten Namen mit', async () => {
    aufbau()
    const u = user()
    await u.type(await screen.findByLabelText('Telefon (optional)'), '+43 1 234')

    // Der vorbelegte Name ist ein Vorschlag und keine Vorgabe: wer im Verein
    // unter einem anderen antritt, ändert ihn hier.
    await u.clear(screen.getByLabelText('Nachname'))
    await u.type(screen.getByLabelText('Nachname'), 'Moser-Huber')

    await u.click(screen.getByRole('button', { name: 'Melden und beitreten' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/join/tok-abcdef')).toMatchObject({
        phone: '+43 1 234',
        lastName: 'Moser-Huber',
      }),
    )
  })

  it('sperrt das Melden, solange der Name fehlt', async () => {
    const u = user()
    aufbau()

    await u.clear(await screen.findByLabelText('Vorname'))
    expect(screen.getByRole('button', { name: 'Melden und beitreten' })).toBeDisabled()
  })

  it('sagt auf der Warteliste, warum', async () => {
    server.use(
      http.post('/api/join/:token', () =>
        HttpResponse.json({ tournamentId: fx.IDS.tournament, entryId: fx.IDS.entry1, status: 2 }),
      ),
    )
    aufbau()
    await screen.findByLabelText('Vorname')

    await user().click(screen.getByRole('button', { name: 'Melden und beitreten' }))

    expect(await screen.findByText('Auf der Warteliste')).toBeInTheDocument()
    expect(screen.getByText(/Das Feld war voll/)).toBeInTheDocument()
  })

  it('nennt einen abgewiesenen Beitritt beim Namen', async () => {
    server.use(
      http.post('/api/join/:token', () =>
        HttpResponse.json(
          { detail: 'Das Turnier verlangt einen Partner.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByLabelText('Vorname')

    await user().click(screen.getByRole('button', { name: 'Melden und beitreten' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Das Turnier verlangt einen Partner.',
    )
  })

  it('deutet ein 404 beim Absenden als erneuerten Link', async () => {
    server.use(http.post('/api/join/:token', () => new HttpResponse(null, { status: 404 })))
    aufbau()
    await screen.findByLabelText('Vorname')

    await user().click(screen.getByRole('button', { name: 'Melden und beitreten' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/Vielleicht wurde er erneuert/)
  })
})

describe('JoinScreen — im Doppel', () => {
  it('fragt nach dem Partner und schickt ihn mit', async () => {
    db.join = fx.joinView({ needsPartner: true })
    const u = user()
    aufbau()

    await u.type(await screen.findByLabelText('Vorname des Partners'), 'Eva')
    await u.type(screen.getByLabelText('Nachname des Partners'), 'Berger')
    await u.type(screen.getByLabelText('E-Mail des Partners (optional)'), 'eva@example.invalid')
    await u.type(screen.getByLabelText('Teamname (optional)'), 'Die Netzroller')

    await u.click(screen.getByRole('button', { name: 'Melden und beitreten' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/join/tok-abcdef')).toMatchObject({
        partnerFirstName: 'Eva',
        partnerLastName: 'Berger',
        partnerEmail: 'eva@example.invalid',
        teamName: 'Die Netzroller',
      }),
    )
  })

  it('lässt Adresse und Teamname des Partners weg, wo nichts steht', async () => {
    // Wer sich zu zweit meldet, hat die Adresse des Partners oft nicht zur
    // Hand. Das darf die Meldung nicht verhindern.
    db.join = fx.joinView({ needsPartner: true })
    const u = user()
    aufbau()

    await u.type(await screen.findByLabelText('Vorname des Partners'), 'Eva')
    await u.type(screen.getByLabelText('Nachname des Partners'), 'Berger')

    await u.click(screen.getByRole('button', { name: 'Melden und beitreten' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/join/tok-abcdef')).toMatchObject({
        partnerEmail: null,
        teamName: null,
      }),
    )
  })

  it('sagt, dass der Partner kein Konto braucht', async () => {
    db.join = fx.joinView({ needsPartner: true })
    aufbau()

    expect(await screen.findByText(/Dein Partner braucht kein Konto/)).toBeInTheDocument()
  })

  it('sperrt das Melden, solange der Partner fehlt', async () => {
    db.join = fx.joinView({ needsPartner: true })
    aufbau()

    await screen.findByLabelText('Vorname des Partners')
    expect(screen.getByRole('button', { name: 'Melden und beitreten' })).toBeDisabled()
  })
})

describe('JoinScreen — nur dazugehören', () => {
  it('tritt bei, ohne zu melden', async () => {
    // Der Partner ohne eigene Meldung, der Vereinskollege, der nur den
    // Spielplan sehen will: sie gehören genauso dazu.
    aufbau()
    await screen.findByLabelText('Vorname')

    await user().click(screen.getByRole('button', { name: 'Nur beitreten, ohne mitzuspielen' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/join/tok-abcdef')).toMatchObject({
        play: false,
        firstName: null,
        lastName: null,
      }),
    )

    expect(await screen.findByText('Du bist dabei')).toBeInTheDocument()
    expect(screen.getByText(/gemeldet bist du nicht/)).toBeInTheDocument()
  })

  it('bietet bei geschlossener Meldung nur noch den Beitritt an', async () => {
    db.join = fx.joinView({ isOpen: false })
    aufbau()

    expect(await screen.findByText(/Die Meldung ist zu/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Vorname')).not.toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Beitreten' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/join/tok-abcdef')).toMatchObject({ play: false }),
    )
  })

  it('sagt dem Mitglied, dass es schon dabei ist — und lässt es hinein', async () => {
    db.join = fx.joinView({ alreadyMember: true })
    const onJoined = aufbau()

    expect(await screen.findByText(/Du gehörst schon dazu/)).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Turnier öffnen' }))
    expect(onJoined).toHaveBeenCalledWith(fx.IDS.tournament)
  })

  it('lässt das Mitglied trotzdem melden — nur eben nicht ein zweites Mal beitreten', async () => {
    // Dazugehören und gemeldet sein ist zweierlei. Wer über den Link kommt und
    // schon Mitglied ist, will in aller Regel genau das eine: mitspielen.
    db.join = fx.joinView({ alreadyMember: true })
    aufbau()

    await screen.findByLabelText('Vorname')
    expect(screen.getByRole('button', { name: 'Melden' })).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Nur beitreten, ohne mitzuspielen' }),
    ).not.toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Melden' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/join/tok-abcdef')).toMatchObject({ play: true }),
    )
  })

  it('bietet dem Mitglied bei geschlossener Meldung gar nichts mehr an', async () => {
    // Ein Knopf „Beitreten" für jemanden, der drin ist, wäre eine Zusage ohne
    // Inhalt — der Weg hinein steht oben.
    db.join = fx.joinView({ alreadyMember: true, isOpen: false })
    aufbau()

    expect(await screen.findByText(/Du gehörst schon dazu/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Beitreten' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Turnier öffnen' })).toBeInTheDocument()
  })
})
