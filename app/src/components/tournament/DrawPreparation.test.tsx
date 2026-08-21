import { screen, waitFor, within } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { Discipline, EntryStatus, TournamentState } from '../../api/types'
import * as fx from '../../test/fixtures'
import { renderWithProviders, user } from '../../test/render'
import { callsTo, db, lastBody, server } from '../../test/server'
import { Toast } from '../layout/Toast'
import { DrawPreparation } from './DrawPreparation'

const T = fx.IDS.tournament

function aufbau(over: Parameters<typeof fx.tournamentDetail>[0] = {}) {
  const onChanged = vi.fn(() => Promise.resolve())
  renderWithProviders(
    <>
      <DrawPreparation
        tournament={fx.tournamentDetail({ state: TournamentState.RegistrationOpen, ...over })}
        onChanged={onChanged}
      />
      <Toast />
    </>,
    { workspace: null },
  )
  return onChanged
}

describe('DrawPreparation — Meldeliste', () => {
  it('zählt die Meldungen und nennt den Zweck der Warteliste', () => {
    aufbau()

    expect(screen.getByText('Meldungen · 4')).toBeInTheDocument()
    expect(screen.getByText(/damit ein volles Feld nicht stillschweigend überläuft/)).toBeInTheDocument()
  })

  it('lässt Zurückgezogene aus der Liste', () => {
    aufbau({
      entries: [
        fx.entry(),
        fx.entry({ id: fx.IDS.entry2, participantName: 'Weg', status: EntryStatus.Withdrawn }),
      ],
    })

    expect(screen.getByText('Meldungen · 1')).toBeInTheDocument()
    expect(screen.queryByText('Weg')).not.toBeInTheDocument()
  })

  it('sagt es, wenn noch niemand gemeldet hat', () => {
    aufbau({ entries: [] })
    expect(screen.getByText('Noch keine Meldung.')).toBeInTheDocument()
  })

  it('zeigt den Setzplatz, wo einer vergeben ist', () => {
    aufbau({ entries: [fx.entry({ seed: 3 })] })
    expect(screen.getByText('[3]')).toBeInTheDocument()
  })

  it('bietet „Annehmen" nur bei einer offenen Meldung an', async () => {
    const onChanged = aufbau({
      entries: [
        fx.entry({ status: EntryStatus.Applied, participantName: 'Neu gemeldet' }),
        fx.entry({ id: fx.IDS.entry2, participantName: 'Schon im Feld', status: EntryStatus.Accepted }),
      ],
    })

    expect(screen.getAllByRole('button', { name: 'Annehmen' })).toHaveLength(1)

    await user().click(screen.getByRole('button', { name: 'Annehmen' }))

    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${T}/entries/${fx.IDS.entry1}/accept`)).toBe(1),
    )
    expect(onChanged).toHaveBeenCalled()
    expect(await screen.findByRole('status')).toHaveTextContent('Meldung angenommen')
  })
})

describe('DrawPreparation — Melden von Hand', () => {
  it('sagt im Entwurf, dass zuerst die Meldung zu öffnen ist', () => {
    aufbau({ state: TournamentState.Draft })

    expect(screen.getByText('Zuerst die Meldung öffnen.')).toBeInTheDocument()
    expect(screen.queryByPlaceholderText('Bestehenden Spieler suchen …')).not.toBeInTheDocument()
  })

  it('verweist bei geschlossener Meldung auf den Ablauf', () => {
    aufbau({ state: TournamentState.RegistrationClosed })
    expect(
      screen.getByText('Die Meldung ist geschlossen. Im Ablauf lässt sie sich wieder öffnen.'),
    ).toBeInTheDocument()
  })

  it('nennt beim Einzel die Ausschreibung als Grund', () => {
    aufbau()

    expect(screen.getByText('Teilnehmer melden · Einzel')).toBeInTheDocument()
    expect(screen.getByText(/eine Meldung zu zweit weist die Domäne ab/)).toBeInTheDocument()
    expect(screen.getByText('Spieler')).toBeInTheDocument()
  })

  it('verlangt beim Doppel zwei Spieler und bietet einen Teamnamen an', () => {
    aufbau({ discipline: Discipline.Doubles })

    expect(screen.getByText('Teilnehmer melden · Doppel')).toBeInTheDocument()
    expect(screen.getByText('Spieler 1')).toBeInTheDocument()
    expect(screen.getByText('Spieler 2')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('Die Netzroller')).toBeInTheDocument()
  })

  it('sagt, was zum Melden noch fehlt', async () => {
    aufbau({ discipline: Discipline.Doubles })

    expect(screen.getByText('Zuerst einen Spieler suchen oder unten neu anlegen.')).toBeInTheDocument()

    await waehleSpieler('Spieler 1', 'S. Moser')

    expect(await screen.findByText('Für ein Doppel fehlt noch der zweite Spieler.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Doppel melden' })).toBeDisabled()
  })

  it('sucht erst ab zwei Zeichen', async () => {
    aufbau()
    const feld = screen.getByPlaceholderText('Bestehenden Spieler suchen …')

    await user().type(feld, 'M')
    expect(callsTo('GET', '/api/players')).toBe(0)

    await user().type(feld, 'o')
    await waitFor(() => expect(callsTo('GET', '/api/players')).toBeGreaterThan(0))
  })

  it('sagt es, wenn die Suche niemanden findet', async () => {
    aufbau()

    await user().type(screen.getByPlaceholderText('Bestehenden Spieler suchen …'), 'Zzz')

    expect(await screen.findByText('Niemand gefunden — unten neu anlegen.')).toBeInTheDocument()
  })

  it('meldet einen Einzelteilnehmer über drei Schritte', async () => {
    const onChanged = aufbau()

    await waehleSpieler('Spieler', 'S. Moser')
    await user().click(screen.getByRole('button', { name: 'Melden' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/participants')).toEqual({
        firstPlayerId: fx.IDS.player1,
        secondPlayerId: null,
        teamName: null,
      }),
    )
    expect(lastBody('POST', `/api/tournaments/${T}/entries`)).toEqual({
      participantId: fx.IDS.participant3,
      seed: null,
    })
    await waitFor(() =>
      expect(callsTo('POST', `/api/tournaments/${T}/entries/${fx.IDS.entry3}/accept`)).toBe(1),
    )
    expect(onChanged).toHaveBeenCalled()
    expect(await screen.findByRole('status')).toHaveTextContent('Gemeldet und angenommen · A. Huber')
  })

  it('meldet ein Doppel mit Teamnamen', async () => {
    aufbau({ discipline: Discipline.Doubles })

    await waehleSpieler('Spieler 1', 'S. Moser')
    await waehleSpieler('Spieler 2', 'L. Berger')
    await user().type(screen.getByPlaceholderText('Die Netzroller'), '  Die Zwei  ')
    await user().click(screen.getByRole('button', { name: 'Doppel melden' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/participants')).toEqual({
        firstPlayerId: fx.IDS.player1,
        secondPlayerId: fx.IDS.player2,
        teamName: 'Die Zwei',
      }),
    )
  })

  it('schickt einen leeren Teamnamen nicht mit', async () => {
    aufbau({ discipline: Discipline.Mixed })

    await waehleSpieler('Spieler 1', 'S. Moser')
    await waehleSpieler('Spieler 2', 'L. Berger')
    await user().type(screen.getByPlaceholderText('Die Netzroller'), '   ')
    await user().click(screen.getByRole('button', { name: 'Doppel melden' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/participants')).toMatchObject({ teamName: null }),
    )
  })

  it('bietet den schon gewählten Spieler nicht ein zweites Mal an', async () => {
    aufbau({ discipline: Discipline.Doubles })

    await waehleSpieler('Spieler 1', 'S. Moser')

    const zweiter = slot('Spieler 2')
    await user().type(within(zweiter).getByPlaceholderText('Bestehenden Spieler suchen …'), 'er')

    await waitFor(() =>
      expect(within(zweiter).getByRole('button', { name: 'L. Berger' })).toBeInTheDocument(),
    )
    expect(within(zweiter).queryByRole('button', { name: 'S. Moser' })).not.toBeInTheDocument()
  })

  it('lässt einen gewählten Spieler wieder tauschen', async () => {
    aufbau()

    await waehleSpieler('Spieler', 'S. Moser')
    await user().click(screen.getByRole('button', { name: 'Ändern' }))

    expect(screen.getByPlaceholderText('Bestehenden Spieler suchen …')).toBeInTheDocument()
  })

  it('legt einen neuen Spieler sofort an', async () => {
    aufbau()
    const u = user()

    await u.type(screen.getByPlaceholderText('Vorname'), 'Anna')
    await u.type(screen.getByPlaceholderText('Nachname'), 'Müller')
    await u.click(screen.getByRole('button', { name: 'Neu' }))

    await waitFor(() =>
      expect(lastBody('POST', '/api/players')).toEqual({
        firstName: 'Anna',
        lastName: 'Müller',
        email: null,
        phone: null,
        dateOfBirth: null,
      }),
    )
    expect(await screen.findByText('Müller, Anna')).toBeInTheDocument()
  })

  it('verlangt für einen neuen Spieler beide Namen', async () => {
    aufbau()
    const u = user()

    await u.type(screen.getByPlaceholderText('Vorname'), 'Anna')
    await u.click(screen.getByRole('button', { name: 'Neu' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Spieler: Vor- und Nachname werden gebraucht.',
    )
    expect(callsTo('POST', '/api/players')).toBe(0)
  })

  it('meldet einen abgewiesenen Spieler', async () => {
    server.use(
      http.post('/api/players', () =>
        HttpResponse.json(
          { detail: 'Diesen Spieler gibt es schon.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    const u = user()

    await u.type(screen.getByPlaceholderText('Vorname'), 'Anna')
    await u.type(screen.getByPlaceholderText('Nachname'), 'Müller')
    await u.click(screen.getByRole('button', { name: 'Neu' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Spieler anlegen: Diesen Spieler gibt es schon.',
    )
  })

  it('meldet eine abgewiesene Meldung', async () => {
    server.use(
      http.post(`/api/tournaments/${T}/entries`, () =>
        HttpResponse.json(
          { detail: 'Das Feld ist voll.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()

    await waehleSpieler('Spieler', 'S. Moser')
    await user().click(screen.getByRole('button', { name: 'Melden' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Melden: Das Feld ist voll.')
  })

  it('sperrt, solange gemeldet wird', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.post('/api/participants', async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return HttpResponse.json({
          id: fx.IDS.participant3,
          displayName: 'A. Huber',
          playerIds: [fx.IDS.player1],
        })
      }),
    )
    aufbau()

    await waehleSpieler('Spieler', 'S. Moser')
    await user().click(screen.getByRole('button', { name: 'Melden' }))

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Wird gemeldet …' })).toBeDisabled(),
    )

    freigeben()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Melden' })).toBeInTheDocument())
  })

  it('zeigt an, dass gesucht wird', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.get('/api/players', async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return HttpResponse.json(db.players)
      }),
    )
    aufbau()

    await user().type(screen.getByPlaceholderText('Bestehenden Spieler suchen …'), 'Mo')

    expect(await screen.findByText('Wird gesucht …')).toBeInTheDocument()
    freigeben()
    await waitFor(() => expect(screen.getByRole('button', { name: 'S. Moser' })).toBeInTheDocument())
  })
})

/**
 * Der Bereich eines Spielerplatzes.
 *
 * Über die Beschriftung und nicht über einen Index: sobald der erste Spieler
 * steht, verschwindet dessen Suchfeld, und jeder Index verschiebt sich.
 */
function slot(label: string): HTMLElement {
  const eyebrow = screen.getByText(label, { selector: '.md-eyebrow' })
  const container = eyebrow.parentElement
  if (!container) throw new Error(`Kein Bereich für „${label}".`)
  return container
}

/** Einen Spieler über die Suche wählen. */
async function waehleSpieler(label: string, name: string): Promise<void> {
  const u = user()
  const bereich = slot(label)

  await u.type(within(bereich).getByPlaceholderText('Bestehenden Spieler suchen …'), name.slice(3, 6))
  await u.click(await within(bereich).findByRole('button', { name }))
}
