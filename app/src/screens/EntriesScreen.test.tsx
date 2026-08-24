import { screen, waitFor, within } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import {
  Discipline,
  EntryOrigin,
  EntryStatus,
  Role,
  TeamFormation,
  TournamentState,
} from '../api/types'
import * as fx from '../test/fixtures'
import { renderWithProviders, user, workspace } from '../test/render'
import { callsTo, db, lastBody, server } from '../test/server'
import { Toast } from '../components/layout/Toast'
import { EntriesScreen } from './EntriesScreen'

const T = fx.IDS.tournament

function aufbau(
  mitTurnier = true,
  over: Parameters<typeof fx.tournamentDetail>[0] = {},
) {
  const reloadTournament = vi.fn(() => Promise.resolve())
  renderWithProviders(
    <>
      <EntriesScreen />
      <Toast />
    </>,
    {
      workspace: workspace({
        tournament: mitTurnier ? fx.tournamentDetail(over) : null,
        reloadTournament,
      }),
    },
  )
  return { reloadTournament }
}

/** Die Zeile, in der dieser Teilnehmer steht. */
function zeile(name: string): HTMLElement {
  const row = screen.getByText(name).closest('.md-checkrow')
  if (!row) throw new Error(`Keine Zeile für „${name}".`)
  return row as HTMLElement
}

describe('EntriesScreen — ohne Turnier', () => {
  it('verweist auf die Turnierliste', () => {
    aufbau(false)

    expect(screen.getByText('Kein Turnier')).toBeInTheDocument()
    expect(screen.getByText(/Oben links unter/)).toBeInTheDocument()
    expect(callsTo('GET', `/api/tournaments/${T}/entries`)).toBe(0)
  })
})

describe('EntriesScreen — Anmeldelink', () => {
  it('zeigt Link, Zählstand und offene Kapazität', async () => {
    db.registration = fx.registrationDetail({ capacity: null })
    aufbau()

    const feld = await screen.findByLabelText('Anmeldelink')
    expect(feld).toHaveValue(`${window.location.origin}/?r=tok-abcdef`)
    expect(
      screen.getByText('3 gemeldet · 4 im Feld · 1 Warteliste · Kapazität offen'),
    ).toBeInTheDocument()
  })

  it('nennt eine gesetzte Kapazität', async () => {
    aufbau()
    expect(await screen.findByText(/· Kapazität 16$/)).toBeInTheDocument()
  })

  it('markiert den Link beim Fokussieren', async () => {
    aufbau()
    const feld = (await screen.findByLabelText('Anmeldelink')) as HTMLInputElement
    const select = vi.spyOn(feld, 'select')

    feld.focus()
    expect(select).toHaveBeenCalled()
  })

  it('speichert Kapazität und Meldeschluss', async () => {
    aufbau()
    await screen.findByLabelText('Anmeldelink')
    const u = user()

    await u.type(screen.getByLabelText('Kapazität (leer = offen)'), '8')
    await u.type(screen.getByLabelText('Meldeschluss (leer = offen)'), '2026-05-10T22:00')
    await u.click(screen.getByRole('button', { name: 'Bedingungen speichern' }))

    await waitFor(() => {
      const body = lastBody('PUT', `/api/tournaments/${T}/registration`) as {
        capacity: number
        deadline: string
      }
      expect(body.capacity).toBe(8)
      expect(body.deadline).toMatch(/^2026-05-10T/)
    })
    expect(await screen.findByRole('status')).toHaveTextContent('Bedingungen gespeichert')
  })

  it('lässt beides leer, wo nichts eingetragen ist', async () => {
    aufbau()
    await screen.findByLabelText('Anmeldelink')

    await user().click(screen.getByRole('button', { name: 'Bedingungen speichern' }))

    await waitFor(() =>
      expect(lastBody('PUT', `/api/tournaments/${T}/registration`)).toEqual({
        capacity: null,
        deadline: null,
      }),
    )
  })

  it('meldet einen abgewiesenen Zustand', async () => {
    server.use(
      http.put(`/api/tournaments/${T}/registration`, () =>
        HttpResponse.json(
          { detail: 'Kapazität unter dem Feld.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByLabelText('Anmeldelink')

    await user().click(screen.getByRole('button', { name: 'Bedingungen speichern' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Speichern: Kapazität unter dem Feld.',
    )
  })

  it('erneuert den Link und sagt, was das für den alten heißt', async () => {
    aufbau()
    await screen.findByLabelText('Anmeldelink')

    await user().click(screen.getByRole('button', { name: 'Erneuern' }))

    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${T}/registration/link/rotate`)).toBe(1),
    )
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Neuer Anmeldelink — der alte ist ab sofort wertlos',
    )
  })

  it('meldet einen gescheiterten Wechsel', async () => {
    server.use(
      http.post(`/api/tournaments/${T}/registration/link/rotate`, () =>
        HttpResponse.json(
          { detail: 'Geht nicht.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByLabelText('Anmeldelink')

    await user().click(screen.getByRole('button', { name: 'Erneuern' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Link erneuern: Geht nicht.')
  })

  it('zeigt gar nichts, solange der Link nicht geladen ist', () => {
    aufbau()
    expect(screen.queryByLabelText('Anmeldelink')).not.toBeInTheDocument()
  })
})

describe('EntriesScreen — Rollen', () => {
  it('nennt, wer mitarbeitet, mit Name und E-Mail', async () => {
    aufbau()

    expect(await screen.findByText('Wer mitarbeitet')).toBeInTheDocument()
    expect(screen.getByText('Rudi Turnierleitung')).toBeInTheDocument()
    expect(screen.getByText('· rudi@example.invalid')).toBeInTheDocument()
    // Einmal als Chip an der Zeile, einmal als Option im Auswahlfeld.
    expect(screen.getAllByText('Turnierleitung')).toHaveLength(2)
  })

  it('nimmt die E-Mail als Namen, wo keiner da ist', async () => {
    db.roles = [fx.tournamentRole({ displayName: null })]
    aufbau()

    expect(await screen.findByText('rudi@example.invalid')).toBeInTheDocument()
  })

  it('nimmt die Kennung, wo auch die E-Mail fehlt', async () => {
    db.roles = [fx.tournamentRole({ displayName: null, email: null })]
    aufbau()

    expect(await screen.findByText(fx.IDS.user)).toBeInTheDocument()
  })

  it('schützt die letzte Turnierleitung vor dem Entzug', async () => {
    aufbau()
    await screen.findByText('Wer mitarbeitet')

    const knopf = screen.getByRole('button', { name: 'Entziehen' })
    expect(knopf).toBeDisabled()
    expect(knopf).toHaveAttribute(
      'title',
      'Die letzte Turnierleitung lässt sich nicht entziehen — ohne sie sähe niemand mehr dieses Turnier.',
    )
  })

  it('entzieht eine Rolle, wo es geht', async () => {
    db.roles = [
      fx.tournamentRole(),
      fx.tournamentRole({
        assignmentId: 'r0000000-0000-0000-0000-000000000009',
        displayName: 'Schieds Richter',
        email: 'referee@example.invalid',
        role: Role.Referee,
      }),
    ]
    aufbau()
    await screen.findByText('Schieds Richter')

    const knoepfe = screen.getAllByRole('button', { name: 'Entziehen' })
    expect(knoepfe[1]).not.toBeDisabled()

    await user().click(knoepfe[1]!)

    await waitFor(() =>
      expect(
        callsTo('DELETE', `/api/tournaments/${T}/roles/r0000000-0000-0000-0000-000000000009`),
      ).toBe(1),
    )
    expect(await screen.findByRole('status')).toHaveTextContent('Rolle entzogen')
  })

  it('beruft über die E-Mail-Adresse', async () => {
    aufbau()
    await screen.findByText('Wer mitarbeitet')
    const u = user()

    expect(screen.getByRole('button', { name: 'Berufen' })).toBeDisabled()

    await u.type(screen.getByLabelText('E-Mail-Adresse'), '  neu@example.invalid  ')
    await u.selectOptions(screen.getByLabelText('Rolle'), String(Role.TournamentDirector))
    await u.click(screen.getByRole('button', { name: 'Berufen' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/tournaments/${T}/roles`)).toEqual({
        email: 'neu@example.invalid',
        role: Role.TournamentDirector,
      }),
    )
    expect(screen.getByLabelText('E-Mail-Adresse')).toHaveValue('')
  })

  it('bietet nur die beiden Rollen an, die es am Turnier gibt', async () => {
    aufbau()
    await screen.findByText('Wer mitarbeitet')

    const optionen = within(screen.getByLabelText('Rolle')).getAllByRole('option')
    expect(optionen.map((o) => o.textContent)).toEqual(['Schiedsrichter', 'Turnierleitung'])
  })

  it('meldet eine abgewiesene Berufung', async () => {
    server.use(
      http.post(`/api/tournaments/${T}/roles`, () =>
        HttpResponse.json(
          { detail: 'Kein Konto mit dieser Adresse.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByText('Wer mitarbeitet')

    await user().type(screen.getByLabelText('E-Mail-Adresse'), 'neu@example.invalid')
    await user().click(screen.getByRole('button', { name: 'Berufen' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Berufen: Kein Konto mit dieser Adresse.',
    )
  })

  it('meldet einen Fehler beim Laden und bietet einen zweiten Anlauf', async () => {
    server.use(
      http.get(`/api/tournaments/:id/roles`, () => new HttpResponse(null, { status: 503 })),
    )
    aufbau()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await user().click(screen.getAllByRole('button', { name: 'Erneut versuchen' })[0]!)
  })
})

describe('EntriesScreen — Meldungen', () => {
  it('zählt gemeldet, im Feld und Warteliste', async () => {
    db.entries = [
      fx.entryOverview({ status: EntryStatus.Applied }),
      fx.entryOverview({ id: fx.IDS.entry2, participantName: 'L. Berger', status: EntryStatus.Accepted }),
      fx.entryOverview({ id: fx.IDS.entry3, participantName: 'A. Huber', status: EntryStatus.WaitingList }),
    ]
    aufbau()

    await screen.findByText('A. Huber')
    expect([...document.querySelectorAll('.md-kpi')].map((el) => el.textContent)).toEqual([
      '1gemeldet',
      '1im Feld',
      '1Warteliste',
    ])
  })

  it('nennt Herkunft, Zeitpunkt und Bestätigungscode', async () => {
    db.entries = [fx.entryOverview({ origin: EntryOrigin.SelfService })]
    aufbau()

    await screen.findByText('S. Moser')
    expect(zeile('S. Moser')).toHaveTextContent('Selbstmeldung')
    expect(zeile('S. Moser')).toHaveTextContent('ABC123')
  })

  it('nennt die Turnierleitung als Herkunft, wo sie erfasst hat', async () => {
    aufbau()
    await screen.findByText('S. Moser')
    expect(zeile('S. Moser')).toHaveTextContent('von der Turnierleitung')
  })

  it('lässt den Code weg, wo keiner mitkommt', async () => {
    db.entries = [fx.entryOverview({ confirmationCode: null })]
    aufbau()

    await screen.findByText('S. Moser')
    expect(zeile('S. Moser')).not.toHaveTextContent('ABC123')
  })

  it('zeigt Kontaktdaten, wenn das Backend sie mitschickt', async () => {
    aufbau()
    await screen.findByText('S. Moser')

    expect(zeile('S. Moser')).toHaveTextContent('S. Moser · moser@example.invalid · +43 1 234')
  })

  it('zeigt keine, wo keine mitkommen', async () => {
    db.entries = [fx.entryOverview({ contacts: [] })]
    aufbau()

    await screen.findByText('S. Moser')
    expect(zeile('S. Moser')).not.toHaveTextContent('moser@example.invalid')
  })

  it('nimmt eine Meldung an und lädt alles zusammen nach', async () => {
    db.entries = [fx.entryOverview({ status: EntryStatus.Applied })]
    const { reloadTournament } = aufbau()
    await screen.findByText('S. Moser')

    await user().click(within(zeile('S. Moser')).getByRole('button', { name: 'Annehmen' }))

    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${T}/entries/${fx.IDS.entry1}/accept`)).toBe(1),
    )
    expect(reloadTournament).toHaveBeenCalled()
    expect(await screen.findByRole('status')).toHaveTextContent('Meldung angenommen')
  })

  it('setzt auf die Warteliste und zieht zurück', async () => {
    db.entries = [fx.entryOverview({ status: EntryStatus.Applied })]
    aufbau()
    await screen.findByText('S. Moser')
    const u = user()

    await u.click(within(zeile('S. Moser')).getByRole('button', { name: 'Warteliste' }))
    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${T}/entries/${fx.IDS.entry1}/waiting-list`)).toBe(1),
    )

    await u.click(within(zeile('S. Moser')).getByRole('button', { name: 'Zurückziehen' }))
    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${T}/entries/${fx.IDS.entry1}/withdraw`)).toBe(1),
    )
  })

  it('sperrt den Zug, der schon gilt', async () => {
    db.entries = [fx.entryOverview({ status: EntryStatus.Accepted })]
    aufbau()
    await screen.findByText('S. Moser')

    expect(within(zeile('S. Moser')).getByRole('button', { name: 'Annehmen' })).toBeDisabled()
    expect(within(zeile('S. Moser')).getByRole('button', { name: 'Warteliste' })).not.toBeDisabled()
  })

  it('speichert die Setzposition beim Verlassen des Feldes', async () => {
    db.entries = [fx.entryOverview({ seed: null })]
    aufbau()
    await screen.findByText('S. Moser')
    const u = user()

    const feld = screen.getByLabelText('Setzposition von S. Moser')
    await u.type(feld, '3')
    await u.tab()

    await waitFor(() =>
      expect(lastBody('PUT', `/api/tournaments/${T}/entries/${fx.IDS.entry1}/seed`)).toEqual({
        seed: 3,
      }),
    )
  })

  it('nimmt eine Setzposition zurück, wo das Feld geleert wird', async () => {
    aufbau()
    await screen.findByText('S. Moser')
    const u = user()

    const feld = screen.getByLabelText('Setzposition von S. Moser')
    await u.clear(feld)
    await u.tab()

    await waitFor(() =>
      expect(lastBody('PUT', `/api/tournaments/${T}/entries/${fx.IDS.entry1}/seed`)).toEqual({
        seed: null,
      }),
    )
  })

  it('schickt nichts, wo sich die Setzposition nicht geändert hat', async () => {
    aufbau()
    await screen.findByText('S. Moser')

    screen.getByLabelText('Setzposition von S. Moser').focus()
    await user().tab()

    expect(callsTo('PUT', `/api/tournaments/${T}/entries/${fx.IDS.entry1}/seed`)).toBe(0)
  })

  it('meldet einen abgewiesenen Zug', async () => {
    db.entries = [fx.entryOverview({ status: EntryStatus.Applied })]
    server.use(
      http.post(`/api/tournaments/${T}/entries/:entryId/accept`, () =>
        HttpResponse.json(
          { detail: 'Das Feld ist voll.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByText('S. Moser')

    await user().click(within(zeile('S. Moser')).getByRole('button', { name: 'Annehmen' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Meldung angenommen: Das Feld ist voll.',
    )
  })

  it('sperrt die Zeile, solange ihr Zug läuft', async () => {
    db.entries = [fx.entryOverview({ status: EntryStatus.Applied })]
    let freigeben: () => void = () => {}
    server.use(
      http.post(`/api/tournaments/${T}/entries/:entryId/accept`, async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return new HttpResponse(null, { status: 204 })
      }),
    )
    aufbau()
    await screen.findByText('S. Moser')

    await user().click(within(zeile('S. Moser')).getByRole('button', { name: 'Annehmen' }))

    await waitFor(() =>
      expect(within(zeile('S. Moser')).getByRole('button', { name: 'Warteliste' })).toBeDisabled(),
    )
    freigeben()
  })

  it('lädt nach einem Import Meldungen und Turnier nach', async () => {
    const { reloadTournament } = aufbau()
    await screen.findByText('S. Moser')

    await user().type(screen.getByLabelText('Teilnehmerliste einfügen'), 'Anna;Müller')
    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    await waitFor(() => expect(reloadTournament).toHaveBeenCalled())
  })

  it('sagt es, wenn noch niemand gemeldet hat', async () => {
    db.entries = []
    aufbau()

    expect(await screen.findByText('Noch keine Meldung')).toBeInTheDocument()
    expect(screen.getByText(/kann sich jeder ohne Konto melden/)).toBeInTheDocument()
  })

  it('zeigt die Ladeanzeige, solange nichts da ist', () => {
    aufbau()
    expect(screen.getByRole('status')).toHaveTextContent('Meldungen werden geladen …')
  })

  it('meldet einen Fehler und bietet einen zweiten Anlauf', async () => {
    server.use(
      http.get(`/api/tournaments/:id/entries`, () => new HttpResponse(null, { status: 503 })),
    )
    aufbau()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
  })

  it('zeigt die Teambildung nur, wo die Turnierleitung sie stellt', async () => {
    aufbau(true, {
      discipline: Discipline.Doubles,
      teamFormation: TeamFormation.ByOrganiser,
    })

    expect(await screen.findByText('Teams')).toBeInTheDocument()

    // Und dort erwartet auch die Liste eine Person je Zeile.
    expect(screen.queryByText(/Partner-Vorname/)).not.toBeInTheDocument()
  })

  it('lädt die Meldungen nach, wenn die Teams gefallen sind', async () => {
    // Die Zuordnung steht an den Meldungen: ohne Nachladen zeigte die Liste
    // weiter Einzelne, die längst ein Team haben.
    const { reloadTournament } = aufbau(true, {
      discipline: Discipline.Doubles,
      teamFormation: TeamFormation.ByOrganiser,
    })

    await screen.findByText('Teams')
    await user().click(screen.getByRole('button', { name: 'Teams auslosen' }))

    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/teams/draw`)).toBe(1))
    await waitFor(() => expect(reloadTournament).toHaveBeenCalled())
    expect(callsTo('GET', `/api/tournaments/${T}/entries`)).toBeGreaterThan(1)
  })

  it('zeigt beim Vereinsdoppel keine Teambildung', async () => {
    aufbau(true, {
      discipline: Discipline.Doubles,
      teamFormation: TeamFormation.Registered,
    })

    expect(await screen.findByText(/Partner-Vorname/)).toBeInTheDocument()
    expect(screen.queryByText('Teams')).not.toBeInTheDocument()
  })

  it('rührt die Teams nach der Auslosung nicht mehr an', async () => {
    aufbau(true, {
      discipline: Discipline.Mixed,
      teamFormation: TeamFormation.ByOrganiser,
      state: TournamentState.DrawGenerated,
    })

    await screen.findByText('Teams')
    expect(screen.getByRole('button', { name: 'Teams auslosen' })).toBeDisabled()
  })
})
