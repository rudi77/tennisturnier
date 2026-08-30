/**
 * Der Kontaktgraph (ADR-0013).
 *
 * Was hier geprüft wird, ist vor allem die Aussage der leeren Liste: sie ist
 * kein Fehler und kein „noch niemand hinzugefügt", sondern „noch nichts
 * gespielt". Es gibt keinen Knopf, der sie füllt.
 */

import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import * as fx from '../test/fixtures'
import { IDS } from '../test/fixtures'
import { db } from '../test/server'
import { renderWithProviders, user } from '../test/render'
import { ConnectionsScreen } from './ConnectionsScreen'

function aufbau() {
  window.history.replaceState({}, '', '/?screen=connections')
  renderWithProviders(<ConnectionsScreen />)
}

describe('ConnectionsScreen', () => {
  it('nennt Bilanz, Turnier und Datum', async () => {
    aufbau()

    expect(await screen.findByText('Berger, Lena')).toBeInTheDocument()
    expect(screen.getByText(/3× gegeneinander · 2:1/)).toBeInTheDocument()
    expect(screen.getByText(/Clubmeisterschaft 2026/)).toBeInTheDocument()
    expect(screen.getByText(/2 gemeinsame Turniere/)).toBeInTheDocument()
  })

  it('trennt Partner von Gegnern', async () => {
    db.connections = [fx.connection({ together: 2, against: 0, won: 0, lost: 0 })]
    aufbau()

    expect(await screen.findByText('2× zusammen im Doppel')).toBeInTheDocument()
    expect(screen.queryByText(/gegeneinander/)).not.toBeInTheDocument()
  })

  it('führt zum Profil des Mitspielers', async () => {
    aufbau()
    await screen.findByText('Berger, Lena')

    await user().click(screen.getByRole('button', { name: 'Berger, Lena' }))

    expect(window.location.search).toContain('screen=profile')
    expect(window.location.search).toContain(`p=${IDS.player2}`)
  })

  it('sagt bei leerer Liste, dass niemand sie füllen muss', async () => {
    db.connections = []
    aufbau()

    expect(await screen.findByText('Noch keine Mitspieler')).toBeInTheDocument()
    expect(screen.getByText(/Hinzufügen muss sie niemand/)).toBeInTheDocument()
  })
})
