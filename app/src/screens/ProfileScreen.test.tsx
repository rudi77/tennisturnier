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
import { db, lastBody } from '../test/server'
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

  it('zeigt, wie viele Zeichen noch frei sind', async () => {
    db.myProfile = fx.playerProfile({ isSelf: true, bio: null })
    aufbau('?screen=profile')
    await screen.findByText('Moser, Sabine')

    await user().click(screen.getByRole('button', { name: 'Profil bearbeiten' }))

    expect(screen.getByText('500 Zeichen frei')).toBeInTheDocument()
  })
})
