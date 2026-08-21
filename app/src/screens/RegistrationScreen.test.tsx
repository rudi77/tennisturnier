import { screen, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Discipline, EntryStatus } from '../api/types'
import * as fx from '../test/fixtures'
import { renderWithProviders, user } from '../test/render'
import { db, lastBody, server } from '../test/server'
import { RegistrationScreen } from './RegistrationScreen'

const TOKEN = 'tok-abcdef'

function aufbau(token = TOKEN) {
  return renderWithProviders(<RegistrationScreen token={token} />, { workspace: null })
}

beforeEach(() => {
  // jsdom kennt kein Scrollen; die Bestätigung springt nach oben.
  window.scrollTo = vi.fn()
})

async function fuelleFormular(): Promise<void> {
  const u = user()
  await u.type(screen.getByRole('textbox', { name: 'Vorname' }), 'Anna')
  await u.type(screen.getByRole('textbox', { name: 'Nachname' }), 'Müller')
  await u.type(screen.getByRole('textbox', { name: 'E-Mail' }), 'anna@example.invalid')
}

describe('RegistrationScreen', () => {
  it('zeigt Turnierkopf, Ort und Termin — und keine Teilnehmerliste', async () => {
    aufbau()

    expect(await screen.findByText('Clubmeisterschaft 2026')).toBeInTheDocument()
    expect(screen.getByText('TC Musterstadt · Musterstadt')).toBeInTheDocument()
    expect(screen.getByText(/16\..*17\. Mai 2026 · Einzel/)).toBeInTheDocument()
    expect(screen.queryByText('S. Moser')).not.toBeInTheDocument()
  })

  it('lässt den Ort ohne Stadt für sich stehen', async () => {
    db.publicRegistration = fx.publicRegistrationView({ city: null })
    aufbau()

    expect(await screen.findByText('TC Musterstadt')).toBeInTheDocument()
  })

  it('nennt den Meldeschluss, wo einer gesetzt ist', async () => {
    aufbau()
    expect(await screen.findByText(/Meldeschluss:/)).toBeInTheDocument()
  })

  it('schweigt über einen Meldeschluss, den es nicht gibt', async () => {
    db.publicRegistration = fx.publicRegistrationView({ deadline: null })
    aufbau()

    await screen.findByText('Clubmeisterschaft 2026')
    expect(screen.queryByText(/Meldeschluss:/)).not.toBeInTheDocument()
  })

  it('sagt beim vollen Feld, dass weiter gemeldet werden kann', async () => {
    db.publicRegistration = fx.publicRegistrationView({ freeSlots: 0 })
    aufbau()

    expect(await screen.findByText(/Das Feld ist voll/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Meldung absenden' })).toBeInTheDocument()
  })

  it('schweigt über ein volles Feld, wo noch Plätze frei sind', async () => {
    aufbau()
    await screen.findByText('Clubmeisterschaft 2026')
    expect(screen.queryByText(/Das Feld ist voll/)).not.toBeInTheDocument()
  })

  it('zeigt bei geschlossener Meldung kein Formular, aber den Weg zurück', async () => {
    db.publicRegistration = fx.publicRegistrationView({ isOpen: false })
    aufbau()

    expect(await screen.findByText('Die Meldung ist geschlossen')).toBeInTheDocument()
    expect(screen.getByText(/gilt derselbe Link erneut/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Meldung absenden' })).not.toBeInTheDocument()
  })

  it('lädt und sagt es', () => {
    aufbau()
    expect(screen.getByRole('status')).toHaveTextContent('Turnier wird geladen …')
  })

  it('erfindet bei einem unbekannten Token keinen Unterschied', async () => {
    aufbau('unbekannt')

    expect(await screen.findByText('Dieser Anmeldelink führt nirgendwohin')).toBeInTheDocument()
    expect(screen.getByText(/oder er wurde erneuert/)).toBeInTheDocument()
  })

  it('zeigt einen anderen Fehler als das, was er ist', async () => {
    server.use(
      http.get('/public/registrations/:token', () => new HttpResponse(null, { status: 503 })),
    )
    aufbau()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
  })

  it('behandelt eine leere Antwort wie einen unbekannten Link', async () => {
    server.use(
      http.get('/public/registrations/:token', () => new HttpResponse(null, { status: 204 })),
    )
    aufbau()

    expect(await screen.findByText('Dieser Anmeldelink führt nirgendwohin')).toBeInTheDocument()
  })
})

describe('RegistrationScreen — Formular', () => {
  it('verlangt Name und E-Mail', async () => {
    aufbau()
    await screen.findByText('Melden')

    expect(screen.getByRole('button', { name: 'Meldung absenden' })).toBeDisabled()

    await fuelleFormular()
    expect(screen.getByRole('button', { name: 'Meldung absenden' })).not.toBeDisabled()
  })

  it('schickt die Meldung ohne Partnerangaben ab', async () => {
    aufbau()
    await screen.findByText('Melden')

    await fuelleFormular()
    await user().type(screen.getByRole('textbox', { name: 'Telefon (optional)' }), ' +43 1 234 ')
    await user().click(screen.getByRole('button', { name: 'Meldung absenden' }))

    await waitFor(() =>
      expect(lastBody('POST', `/public/registrations/${TOKEN}`)).toEqual({
        firstName: 'Anna',
        lastName: 'Müller',
        email: 'anna@example.invalid',
        phone: '+43 1 234',
        partnerFirstName: null,
        partnerLastName: null,
        partnerEmail: null,
        teamName: null,
      }),
    )
  })

  it('lässt eine leere Telefonnummer weg', async () => {
    aufbau()
    await screen.findByText('Melden')

    await fuelleFormular()
    await user().type(screen.getByRole('textbox', { name: 'Telefon (optional)' }), '   ')
    await user().click(screen.getByRole('button', { name: 'Meldung absenden' }))

    await waitFor(() =>
      expect(lastBody('POST', `/public/registrations/${TOKEN}`)).toMatchObject({ phone: null }),
    )
  })

  it('verlangt beim Doppel den Partner', async () => {
    db.publicRegistration = fx.publicRegistrationView({
      discipline: Discipline.Doubles,
      needsPartner: true,
    })
    aufbau()

    expect(await screen.findByText('Als Doppel melden')).toBeInTheDocument()

    await fuelleFormular()
    expect(screen.getByRole('button', { name: 'Meldung absenden' })).toBeDisabled()

    const u = user()
    await u.type(screen.getByRole('textbox', { name: 'Vorname des Partners' }), 'Bea')
    await u.type(screen.getByRole('textbox', { name: 'Nachname des Partners' }), 'Berger')

    expect(screen.getByRole('button', { name: 'Meldung absenden' })).not.toBeDisabled()
  })

  it('schickt beim Doppel Partner und Teamname mit', async () => {
    db.publicRegistration = fx.publicRegistrationView({
      discipline: Discipline.Doubles,
      needsPartner: true,
    })
    aufbau()
    await screen.findByText('Als Doppel melden')

    const u = user()
    await fuelleFormular()
    await u.type(screen.getByRole('textbox', { name: 'Vorname des Partners' }), 'Bea')
    await u.type(screen.getByRole('textbox', { name: 'Nachname des Partners' }), 'Berger')
    await u.type(screen.getByRole('textbox', { name: 'E-Mail des Partners (optional)' }), 'bea@example.invalid')
    await u.type(screen.getByRole('textbox', { name: 'Teamname (optional)' }), 'Die Netzroller')
    await u.click(screen.getByRole('button', { name: 'Meldung absenden' }))

    await waitFor(() =>
      expect(lastBody('POST', `/public/registrations/${TOKEN}`)).toMatchObject({
        partnerFirstName: 'Bea',
        partnerLastName: 'Berger',
        partnerEmail: 'bea@example.invalid',
        teamName: 'Die Netzroller',
      }),
    )
  })

  it('lässt leere Partnerangaben weg', async () => {
    db.publicRegistration = fx.publicRegistrationView({
      discipline: Discipline.Doubles,
      needsPartner: true,
    })
    aufbau()
    await screen.findByText('Als Doppel melden')

    const u = user()
    await fuelleFormular()
    await u.type(screen.getByRole('textbox', { name: 'Vorname des Partners' }), 'Bea')
    await u.type(screen.getByRole('textbox', { name: 'Nachname des Partners' }), 'Berger')
    await u.click(screen.getByRole('button', { name: 'Meldung absenden' }))

    await waitFor(() =>
      expect(lastBody('POST', `/public/registrations/${TOKEN}`)).toMatchObject({
        partnerEmail: null,
        teamName: null,
      }),
    )
  })

  it('sagt, was mit den Daten geschieht — und was nicht erhoben wird', async () => {
    aufbau()
    expect(await screen.findByText(/Kein Geburtsdatum/)).toBeInTheDocument()
  })

  it('nennt den Bestätigungscode als Weg zurück', async () => {
    aufbau()
    await screen.findByText('Melden')

    await fuelleFormular()
    await user().click(screen.getByRole('button', { name: 'Meldung absenden' }))

    expect(await screen.findByText('Meldung angekommen')).toBeInTheDocument()
    expect(screen.getByText('XYZ789')).toBeInTheDocument()
    expect(screen.getByText(/aufschreiben oder\s+abfotografieren/)).toBeInTheDocument()
    expect(window.scrollTo).toHaveBeenCalled()
  })

  it('sagt es, wenn die Meldung auf der Warteliste landet', async () => {
    server.use(
      http.post('/public/registrations/:token', () =>
        HttpResponse.json({ confirmationCode: 'WL0001', status: EntryStatus.WaitingList }),
      ),
    )
    aufbau()
    await screen.findByText('Melden')

    await fuelleFormular()
    await user().click(screen.getByRole('button', { name: 'Meldung absenden' }))

    expect(await screen.findByText('Auf der Warteliste')).toBeInTheDocument()
    expect(screen.getByText(/die Reihenfolge der Meldungen ist dabei festgehalten/)).toBeInTheDocument()
  })

  it('erklärt einen 404 beim Absenden als „geht gerade nicht"', async () => {
    server.use(
      http.post('/public/registrations/:token', () => new HttpResponse(null, { status: 404 })),
    )
    aufbau()
    await screen.findByText('Melden')

    await fuelleFormular()
    await user().click(screen.getByRole('button', { name: 'Meldung absenden' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Über diesen Link lässt sich gerade nichts melden. Vielleicht ist der Meldeschluss vorbei.',
    )
  })

  it('zeigt die Meldung der Domäne, wo eine kommt', async () => {
    server.use(
      http.post('/public/registrations/:token', () =>
        HttpResponse.json(
          { detail: 'Der Meldeschluss ist vorbei.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByText('Melden')

    await fuelleFormular()
    await user().click(screen.getByRole('button', { name: 'Meldung absenden' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Der Meldeschluss ist vorbei.')
  })

  it('nennt einen unbekannten Fehlschlag beim Namen', async () => {
    server.use(
      http.post('/public/registrations/:token', () => {
        // eslint-disable-next-line @typescript-eslint/only-throw-error
        throw 'kaputt'
      }),
    )
    aufbau()
    await screen.findByText('Melden')

    await fuelleFormular()
    await user().click(screen.getByRole('button', { name: 'Meldung absenden' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('sperrt, solange gesendet wird', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.post('/public/registrations/:token', async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return HttpResponse.json({ confirmationCode: 'XYZ789', status: EntryStatus.Applied })
      }),
    )
    aufbau()
    await screen.findByText('Melden')

    await fuelleFormular()
    await user().click(screen.getByRole('button', { name: 'Meldung absenden' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Wird gesendet …' })).toBeDisabled())

    freigeben()
    expect(await screen.findByText('Meldung angekommen')).toBeInTheDocument()
  })
})
