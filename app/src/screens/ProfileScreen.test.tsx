/**
 * Das Spielerprofil.
 *
 * Zwei Dinge werden hier geprüft: dass die Historie lesbar dasteht — und dass
 * die Seite nicht so tut, als wäre ihre Bilanz absolut. Sie gilt relativ zum
 * Betrachter (ADR-0013), und wer das nicht liest, hält die Zahl für falsch,
 * sobald sie sich nach einem Beitritt ändert.
 */

import { screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as fx from '../test/fixtures'
import { IDS } from '../test/fixtures'
import { callsTo, db, lastBody } from '../test/server'
import { renderWithProviders, user } from '../test/render'
import { Toast } from '../components/layout/Toast'
import { ProfileScreen } from './ProfileScreen'

function aufbau(suche = '') {
  window.history.replaceState({}, '', `/${suche}`)

  const onOpenTournament = vi.fn()

  renderWithProviders(
    <>
      <ProfileScreen onOpenTournament={onOpenTournament} />
      <Toast />
    </>,
  )

  return onOpenTournament
}

describe('ProfileScreen — ein fremdes Profil', () => {
  it('zeigt Name, Heimatverein und die Bilanz', async () => {
    aufbau(`?screen=profile&p=${IDS.player1}`)

    expect(await screen.findByText('Moser, Sabine')).toBeInTheDocument()
    expect(screen.getByText(/TC Musterstadt/)).toBeInTheDocument()

    // Zwei Matches, einer gewonnen, einer verloren, ein Turnier.
    expect(screen.getByText('Matches').previousSibling).toHaveTextContent('2')
    expect(screen.getByText('Siege').previousSibling).toHaveTextContent('1')
    expect(screen.getByText('Niederlagen').previousSibling).toHaveTextContent('1')
  })

  it('sagt, dass die Zahlen relativ zum Betrachter gelten', async () => {
    aufbau(`?screen=profile&p=${IDS.player1}`)
    await screen.findByText('Moser, Sabine')

    expect(screen.getByText(/Gerechnet über die Turniere, die du sehen darfst/)).toBeInTheDocument()
  })

  it('nennt zu jedem Match Gegner, Turnier und Satzstand', async () => {
    aufbau(`?screen=profile&p=${IDS.player1}`)
    await screen.findByText('Moser, Sabine')

    expect(screen.getByText('6:4 6:2')).toBeInTheDocument()
    expect(screen.getByText('4:6 6:3 7:10')).toBeInTheDocument()
    expect(screen.getAllByText('Berger, Lena').length).toBeGreaterThan(0)
  })

  it('bietet einem Fremden nichts zum Bearbeiten an', async () => {
    aufbau(`?screen=profile&p=${IDS.player1}`)
    await screen.findByText('Moser, Sabine')

    expect(screen.queryByRole('button', { name: 'Profil bearbeiten' })).not.toBeInTheDocument()
  })

  it('führt über einen Gegner zu dessen Profil', async () => {
    aufbau(`?screen=profile&p=${IDS.player1}`)
    await screen.findByText('Moser, Sabine')

    await user().click(screen.getAllByRole('button', { name: 'Berger, Lena' })[0]!)

    // Als Überschrift und nicht als Text: „Berger, Lena" steht auch in den
    // Matchzeilen, und der Titel ist die Aussage, dass wirklich gewechselt wurde.
    expect(await screen.findByRole('heading', { name: 'Berger, Lena' })).toBeInTheDocument()
    expect(window.location.search).toContain(`p=${IDS.player2}`)
  })

  it('führt über ein Turnier in dessen Ablauf', async () => {
    const onOpenTournament = aufbau(`?screen=profile&p=${IDS.player1}`)
    await screen.findByText('Moser, Sabine')

    await user().click(screen.getByRole('button', { name: 'Clubmeisterschaft 2026' }))

    expect(onOpenTournament).toHaveBeenCalledWith(IDS.tournament)
  })

  /**
   * Ein Spieler, mit dem der Aufrufer kein Turnier teilt, existiert für ihn
   * nicht — die API antwortet mit 404, und die Oberfläche darf daraus kein
   * „gibt es nicht" machen (ADR-0004).
   */
  it('behauptet bei 404 nicht, es gäbe den Spieler nicht', async () => {
    aufbau('?screen=profile&p=a0000000-0000-0000-0000-000000000099')

    expect(
      await screen.findByText('Nicht gefunden oder außerhalb der eigenen Turniere'),
    ).toBeInTheDocument()
  })
})

describe('ProfileScreen — was gerechnet wird und was nicht', () => {
  it('nennt ein Turnier ohne gewertetes Match als solches', async () => {
    db.profiles[IDS.player1] = fx.playerProfile({
      matches: [],
      record: {
        played: 0,
        won: 0,
        lost: 0,
        tournaments: 1,
        setsWon: 0,
        setsLost: 0,
        lastPlayedOn: null,
      },
      tournaments: [
        {
          tournamentId: IDS.tournament,
          name: 'Clubmeisterschaft 2026',
          discipline: 0,
          startsOn: '2026-05-16',
          endsOn: '2026-05-17',
          state: 1,
          status: 0,
          participantName: 'Moser, Sabine',
          played: 0,
          won: 0,
        },
      ],
    })
    aufbau(`?screen=profile&p=${IDS.player1}`)

    expect(await screen.findByText(/Noch kein gewertetes Match/)).toBeInTheDocument()
  })

  it('lässt den Turniernamen stehen, wo es nichts zu öffnen gibt', async () => {
    window.history.replaceState({}, '', `/?screen=profile&p=${IDS.player1}`)
    renderWithProviders(<ProfileScreen />)

    await screen.findByText('Moser, Sabine')

    expect(
      screen.queryByRole('button', { name: 'Clubmeisterschaft 2026' }),
    ).not.toBeInTheDocument()
    expect(screen.getByText('Clubmeisterschaft 2026')).toBeInTheDocument()
  })

  it('zeigt bei einem Doppel Partner und beide Gegner', async () => {
    db.profiles[IDS.player1] = fx.playerProfile({
      matches: [
        {
          ...fx.playerProfile().matches[0]!,
          partner: { playerId: IDS.player3, displayName: 'Huber, Anna' },
          opponents: [
            { playerId: IDS.player2, displayName: 'Berger, Lena' },
            { playerId: 'a0000000-0000-0000-0000-000000000004', displayName: 'Klein, Eva' },
          ],
        },
      ],
    })
    aufbau(`?screen=profile&p=${IDS.player1}`)

    await screen.findByText('Moser, Sabine')

    expect(screen.getByText('/')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Klein, Eva' })).toBeInTheDocument()

    // Der Partner führt ebenso ins Profil wie ein Gegner.
    await user().click(screen.getByRole('button', { name: 'Huber, Anna' }))
    expect(window.location.search).toContain(`p=${IDS.player3}`)
  })

  it('nimmt den Namen der Gegenseite, wo keine Spieler benannt sind', async () => {
    db.profiles[IDS.player1] = fx.playerProfile({
      matches: [{ ...fx.playerProfile().matches[0]!, opponents: [], partner: null }],
    })
    aufbau(`?screen=profile&p=${IDS.player1}`)

    await screen.findByText('Moser, Sabine')

    expect(screen.getByText(/Berger, Lena/)).toBeInTheDocument()
  })

  it('lässt Turniere und Matches weg, wo es keine gibt', async () => {
    db.profiles[IDS.player1] = fx.playerProfile({
      tournaments: [],
      matches: [],
      record: {
        played: 3,
        won: 2,
        lost: 1,
        tournaments: 0,
        setsWon: 5,
        setsLost: 3,
        lastPlayedOn: null,
      },
    })
    aufbau(`?screen=profile&p=${IDS.player1}`)

    await screen.findByText('Moser, Sabine')

    // Als Überschrift gesucht: „Turniere" steht auch als Kennzahl im Kopf.
    expect(screen.queryByRole('heading', { name: 'Turniere' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Letzte Matches' })).not.toBeInTheDocument()
  })

  it('sagt einem Fremden, dass es noch nichts Gemeinsames gibt', async () => {
    db.profiles[IDS.player1] = fx.playerProfile({
      tournaments: [],
      matches: [],
      record: {
        played: 0,
        won: 0,
        lost: 0,
        tournaments: 0,
        setsWon: 0,
        setsLost: 0,
        lastPlayedOn: null,
      },
    })
    aufbau(`?screen=profile&p=${IDS.player1}`)

    expect(await screen.findByText('Noch nichts gespielt')).toBeInTheDocument()
    expect(screen.getByText(/noch kein Turnier gemeinsam/)).toBeInTheDocument()
  })
})

describe('ProfileScreen — der Weg zurück', () => {
  it('führt aus einem fremden Profil zum eigenen', async () => {
    aufbau(`?screen=profile&p=${IDS.player1}`)
    await screen.findByText('Moser, Sabine')

    await user().click(screen.getByRole('button', { name: 'Mein Profil' }))

    expect(window.location.search).not.toContain('p=')
  })

  it('führt auch aus einem Fehler zum eigenen Profil zurück', async () => {
    aufbau('?screen=profile&p=a0000000-0000-0000-0000-000000000099')
    await screen.findByText('Nicht gefunden oder außerhalb der eigenen Turniere')

    await user().click(screen.getByRole('button', { name: 'Mein Profil' }))

    expect(window.location.search).not.toContain('p=')
    expect(await screen.findByText('Moser, Sabine')).toBeInTheDocument()
  })

  it('lässt einen Fehler erneut versuchen', async () => {
    aufbau('?screen=profile&p=a0000000-0000-0000-0000-000000000099')
    await screen.findByText('Nicht gefunden oder außerhalb der eigenen Turniere')

    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))

    expect(
      await screen.findByText('Nicht gefunden oder außerhalb der eigenen Turniere'),
    ).toBeInTheDocument()
  })
})

describe('ProfileScreen — das eigene', () => {
  beforeEach(() => {
    window.history.replaceState({}, '', '/?screen=profile')
  })

  it('lädt ohne Spieler-Id das eigene Profil', async () => {
    aufbau('?screen=profile')

    expect(await screen.findByText('Moser, Sabine')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Profil bearbeiten' })).toBeInTheDocument()
  })

  it('bietet ohne eigenen Spieler gleich das Formular an', async () => {
    db.myProfile = null
    aufbau('?screen=profile')

    expect(await screen.findByText('Profil anlegen')).toBeInTheDocument()
    expect(screen.getByText(/Zu deinem Konto gehört noch kein Spieler/)).toBeInTheDocument()
  })

  it('legt beim ersten Speichern den eigenen Spieler an', async () => {
    db.myProfile = null
    aufbau('?screen=profile')
    await screen.findByText('Profil anlegen')

    const u = user()
    await u.type(screen.getByLabelText('Vorname'), 'Anna')
    await u.type(screen.getByLabelText('Nachname'), 'Vogel')
    await u.type(screen.getByLabelText('Heimatverein'), 'TC Hinterbrühl')
    await u.click(screen.getByRole('button', { name: 'Speichern' }))

    await waitFor(() =>
      expect(lastBody('PUT', '/api/me/profile')).toMatchObject({
        firstName: 'Anna',
        lastName: 'Vogel',
        homeClub: 'TC Hinterbrühl',
        bio: null,
      }),
    )

    expect(await screen.findByText('Vogel, Anna')).toBeInTheDocument()
  })

  it('speichert nicht ohne Namen', async () => {
    db.myProfile = null
    aufbau('?screen=profile')
    await screen.findByText('Profil anlegen')

    expect(screen.getByRole('button', { name: 'Speichern' })).toBeDisabled()
  })

  it('speichert eine Änderung am bestehenden Profil', async () => {
    aufbau('?screen=profile')
    await screen.findByText('Moser, Sabine')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Profil bearbeiten' }))

    const ueberMich = await screen.findByLabelText('Über mich')
    await u.clear(ueberMich)
    await u.type(ueberMich, 'Neuer Text.')
    await u.clear(screen.getByLabelText('Heimatverein'))
    await u.click(screen.getByRole('button', { name: 'Speichern' }))

    await waitFor(() =>
      expect(lastBody('PUT', '/api/me/profile')).toMatchObject({
        bio: 'Neuer Text.',
        homeClub: null,
      }),
    )

    // Danach steht wieder die Ansicht und nicht das Formular.
    await waitFor(() =>
      expect(screen.queryByLabelText('Über mich')).not.toBeInTheDocument(),
    )
  })

  it('bricht das Bearbeiten ab, ohne zu speichern', async () => {
    aufbau('?screen=profile')
    await screen.findByText('Moser, Sabine')

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Profil bearbeiten' }))
    await u.click(screen.getByRole('button', { name: 'Abbrechen' }))

    expect(screen.queryByLabelText('Über mich')).not.toBeInTheDocument()
    expect(callsTo('PUT', '/api/me/profile')).toBe(0)
  })

  it('sagt dem eigenen leeren Profil, wie es sich füllt', async () => {
    db.myProfile = fx.playerProfile({
      isSelf: true,
      tournaments: [],
      matches: [],
      record: {
        played: 0,
        won: 0,
        lost: 0,
        tournaments: 0,
        setsWon: 0,
        setsLost: 0,
        lastPlayedOn: null,
      },
    })
    aufbau('?screen=profile')

    expect(await screen.findByText('Noch nichts gespielt')).toBeInTheDocument()
    expect(screen.getByText(/Sobald du für ein Turnier gemeldet bist/)).toBeInTheDocument()
  })

  it('zeigt, wie viele Zeichen noch frei sind', async () => {
    db.myProfile = fx.playerProfile({ isSelf: true, bio: null })
    aufbau('?screen=profile')
    await screen.findByText('Moser, Sabine')

    await user().click(screen.getByRole('button', { name: 'Profil bearbeiten' }))

    expect(screen.getByText('500 Zeichen frei')).toBeInTheDocument()
  })
})
