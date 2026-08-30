/**
 * Der Feed eines Turniers (ADR-0014).
 *
 * Geprüft wird vor allem der Unterschied, auf dem die Entscheidung beruht:
 * Geschriebenes trägt einen Verfasser und lässt sich zurücknehmen, die Chronik
 * trägt keinen und lässt sich nicht ändern.
 */

import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as fx from '../test/fixtures'
import { IDS } from '../test/fixtures'
import { callsTo, db, lastBody } from '../test/server'
import { renderWithProviders, user, workspace } from '../test/render'
import { Toast } from '../components/layout/Toast'
// Der Push-Kanal ist hier eine Attrappe: er hat einen eigenen Test
// (`api/realtime.test.ts`), und jsdom kann den Hub gar nicht erst auflösen. Was
// hier interessiert, ist das Zusammenspiel — dass neu geholt wird, wenn er
// etwas meldet.
const abmelden = vi.fn()
let melde: (() => void) | null = null

vi.mock('../api/realtime', () => ({
  FEED_CHANGED: 'feedChanged',
  subscribeToFeed: (_id: string, onChanged: () => void) => {
    melde = onChanged
    return abmelden
  },
}))

const { FeedScreen } = await import('./FeedScreen')

afterEach(() => {
  melde = null
})

const T = IDS.tournament

function aufbau(mitTurnier = true) {
  window.history.replaceState({}, '', '/?screen=feed')

  renderWithProviders(
    <>
      <FeedScreen />
      <Toast />
    </>,
    { workspace: workspace({ tournament: mitTurnier ? fx.tournamentDetail() : null }) },
  )
}

describe('FeedScreen — ohne Turnier', () => {
  it('verweist auf die Turnierliste und fragt nichts ab', () => {
    aufbau(false)

    expect(screen.getByText('Kein Turnier')).toBeInTheDocument()
    expect(callsTo('GET', `/api/tournaments/${T}/feed`)).toBe(0)
  })
})

describe('FeedScreen — die beiden Hälften', () => {
  it('zeigt Geschriebenes mit Verfasser', async () => {
    aufbau()

    expect(await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rudi Turnierleitung' })).toBeInTheDocument()
  })

  it('zeigt ein Ereignis mit Marke statt Verfasser', async () => {
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    expect(
      screen.getByText('Halbfinale: Moser, Sabine schlägt Berger, Lena 6:4 6:2'),
    ).toBeInTheDocument()
    expect(screen.getByText('Ergebnis')).toBeInTheDocument()
  })

  it('bietet für ein Ereignis kein Zurücknehmen an', async () => {
    db.feed = fx.feedPage({ posts: [fx.feedEvent()] })
    aufbau()
    await screen.findByText('Ergebnis')

    expect(screen.queryByRole('button', { name: 'Zurücknehmen' })).not.toBeInTheDocument()
  })

  it('sagt, wenn noch nichts passiert ist', async () => {
    db.feed = fx.feedPage({ posts: [] })
    aufbau()

    expect(await screen.findByText('Noch nichts passiert')).toBeInTheDocument()
  })
})

describe('FeedScreen — schreiben', () => {
  it('sendet einen Beitrag und zeigt ihn danach', async () => {
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    const u = user()
    await u.type(screen.getByLabelText('Etwas an die Gruppe'), 'Beginn um 14 Uhr.')
    await u.click(screen.getByRole('button', { name: 'Absenden' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/tournaments/${T}/feed`)).toEqual({ text: 'Beginn um 14 Uhr.' }),
    )
    expect(await screen.findByText('Beginn um 14 Uhr.')).toBeInTheDocument()
  })

  it('sendet nichts Leeres', async () => {
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    expect(screen.getByRole('button', { name: 'Absenden' })).toBeDisabled()
  })

  /**
   * Wer nicht schreiben darf, bekommt kein Feld. Das ist keine Sicherheitsgrenze
   * — die steht im Backend —, sondern die Vermeidung einer Schaltfläche, die
   * nichts als einen Fehler auslösen kann.
   */
  it('zeigt ohne Schreibrecht kein Feld', async () => {
    db.feed = fx.feedPage({ canWrite: false })
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    expect(screen.queryByLabelText('Etwas an die Gruppe')).not.toBeInTheDocument()
  })

  it('kommentiert einen Eintrag', async () => {
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    const u = user()
    await u.click(screen.getAllByRole('button', { name: 'Antworten' })[0]!)
    await u.type(screen.getByLabelText('Antwort'), 'Danke für die Info.')
    await u.click(screen.getByRole('button', { name: 'Antwort senden' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/feed/${IDS.post1}/comments`)).toEqual({
        text: 'Danke für die Info.',
      }),
    )
    expect(await screen.findByText('Danke für die Info.')).toBeInTheDocument()
  })

  it('nimmt einen eigenen Beitrag zurück', async () => {
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    await user().click(screen.getByRole('button', { name: 'Zurücknehmen' }))

    await waitFor(() =>
      expect(
        screen.queryByText('Platz 3 ist nass, wir spielen auf 4 weiter.'),
      ).not.toBeInTheDocument(),
    )
  })
})

describe('FeedScreen — der Weg zum Menschen', () => {
  it('führt vom Verfasser ins Profil', async () => {
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    await user().click(screen.getByRole('button', { name: 'Rudi Turnierleitung' }))

    expect(window.location.search).toContain('screen=profile')
    expect(window.location.search).toContain(`p=${IDS.player1}`)
  })

  it('lässt einen Verfasser ohne Spieler als bloßen Namen stehen', async () => {
    db.feed = fx.feedPage({
      posts: [
        fx.feedMessage({
          author: { userId: IDS.user, displayName: 'Nur ein Konto', playerId: null },
        }),
      ],
    })
    aufbau()

    expect(await screen.findByText('Nur ein Konto')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Nur ein Konto' })).not.toBeInTheDocument()
  })
})

describe('FeedScreen — der Push', () => {
  it('holt neu, wenn der Hub etwas meldet', async () => {
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    db.feed = fx.feedPage({ posts: [fx.feedMessage({ text: 'Neu dazugekommen.' })] })
    melde?.()

    expect(await screen.findByText('Neu dazugekommen.')).toBeInTheDocument()
  })
})

describe('FeedScreen — ältere Einträge', () => {
  it('lädt nach, solange es welche gibt', async () => {
    db.feed = fx.feedPage({ before: '2026-05-16T09:00:00+00:00' })
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    await user().click(screen.getByRole('button', { name: 'Ältere anzeigen' }))

    await waitFor(() => expect(callsTo('GET', `/api/tournaments/${T}/feed`)).toBe(2))
  })

  it('bietet nichts an, wo es nichts mehr gibt', async () => {
    aufbau()
    await screen.findByText('Platz 3 ist nass, wir spielen auf 4 weiter.')

    expect(screen.queryByRole('button', { name: 'Ältere anzeigen' })).not.toBeInTheDocument()
  })
})
