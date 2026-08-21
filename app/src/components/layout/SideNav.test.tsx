import { render, screen } from '@testing-library/react'
import type { User } from 'oidc-client-ts'
import { describe, expect, it, vi } from 'vitest'
import * as fx from '../../test/fixtures'
import { user as userEvent } from '../../test/render'
import { SideNav } from './SideNav'

function alsBenutzer(name: string): User {
  return { profile: { name } } as unknown as User
}

function aufbau(over: Partial<Parameters<typeof SideNav>[0]> = {}) {
  const props = {
    screen: 'flow' as const,
    onNavigate: vi.fn(),
    tournament: fx.tournamentDetail(),
    user: alsBenutzer('Sabine Moser'),
    onLogout: vi.fn(),
    ...over,
  }
  render(<SideNav {...props} />)
  return props
}

describe('SideNav', () => {
  it('nummeriert die Punkte in der Reihenfolge, in der sie stehen', () => {
    aufbau()

    const nummern = screen
      .getAllByRole('button')
      .map((b) => b.querySelector('.md-nav__tag')?.textContent)
      .filter(Boolean)

    expect(nummern).toEqual(['01', '02', '03', '04', '05', '06', '07'])
  })

  it('stellt den Ablauf voran — er ist der Einstieg', () => {
    aufbau()
    expect(screen.getAllByRole('button')[0]).toHaveTextContent('Ablauf')
  })

  it('führt die Meldungen vor dem Draw, weil sie ihm vorausgehen', () => {
    aufbau()
    const labels = screen.getAllByRole('button').map((b) => b.textContent ?? '')

    const meldungen = labels.findIndex((l) => l.includes('Meldungen'))
    const draw = labels.findIndex((l) => l.includes('Draw'))

    expect(meldungen).toBeGreaterThanOrEqual(0)
    expect(meldungen).toBeLessThan(draw)
  })

  it('markiert den aktuellen Bildschirm', () => {
    aufbau({ screen: 'board' })

    const aktuell = screen.getAllByRole('button').filter((b) => b.getAttribute('aria-current') === 'page')
    expect(aktuell).toHaveLength(1)
    expect(aktuell[0]).toHaveTextContent('Spielplan')
  })

  it('hält für die Fußleiste eine kurze Beschriftung bereit', () => {
    aufbau()

    const turniere = screen.getAllByRole('button').find((b) => b.textContent?.includes('Meine Turniere'))
    expect(turniere?.querySelector('.md-nav__label--short')).toHaveTextContent('Turniere')

    const ablauf = screen.getAllByRole('button')[0]
    expect(ablauf?.querySelector('.md-nav__label--short')).toHaveTextContent('Ablauf')
  })

  it('navigiert auf Klick', async () => {
    const props = aufbau()
    await userEvent().click(screen.getByText('Live-Ansicht'))
    expect(props.onNavigate).toHaveBeenCalledWith('public')
  })

  it('nennt Ort, Platzanzahl und Zeitzone des geladenen Turniers', () => {
    aufbau()

    expect(screen.getByText('TC Musterstadt')).toBeInTheDocument()
    expect(screen.getByText('2 Plätze · Europe/Vienna')).toBeInTheDocument()
  })

  it('sagt ohne Turnier, dass keines geladen ist', () => {
    aufbau({ tournament: null })

    expect(screen.getByText('—')).toBeInTheDocument()
    expect(screen.getByText('kein Turnier geladen')).toBeInTheDocument()
  })

  it('zeigt Initialen und Namen des Angemeldeten', () => {
    aufbau()

    expect(screen.getByText('SM')).toBeInTheDocument()
    expect(screen.getByText('Sabine Moser')).toBeInTheDocument()
  })

  it('sagt „Nicht angemeldet", wo niemand angemeldet ist', () => {
    aufbau({ user: null })

    expect(screen.getByText('··')).toBeInTheDocument()
    expect(screen.getByText('Nicht angemeldet')).toBeInTheDocument()
  })

  it('meldet ab', async () => {
    const props = aufbau()
    await userEvent().click(screen.getByRole('button', { name: 'Abmelden' }))
    expect(props.onLogout).toHaveBeenCalled()
  })
})
