import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Discipline, TournamentState } from '../api/types'
import * as fx from '../test/fixtures'
import { renderWithProviders, user, workspace } from '../test/render'
import { TournamentsScreen } from './TournamentsScreen'
import { useWorkspace, useWorkspaceOptional } from '../state/WorkspaceContext'
import { renderHook } from '@testing-library/react'

function aufbau(over: Parameters<typeof workspace>[0] = {}) {
  const onCreate = vi.fn()
  const onOpen = vi.fn()
  const ws = workspace(over)

  renderWithProviders(<TournamentsScreen onCreate={onCreate} onOpen={onOpen} />, { workspace: ws })
  return { onCreate, onOpen, ws }
}

describe('TournamentsScreen', () => {
  it('zählt die eigenen Turniere', () => {
    aufbau()
    expect(screen.getByText('1 Turnier')).toBeInTheDocument()

    screen.getByRole('heading', { name: 'Meine Turniere' })
  })

  it('setzt den Plural, wo es mehrere sind', () => {
    aufbau({
      tournaments: [
        fx.tournamentSummary(),
        fx.tournamentSummary({ id: fx.IDS.otherTournament, name: 'Herbstturnier' }),
      ],
    })
    expect(screen.getByText('2 Turniere')).toBeInTheDocument()
  })

  it('nennt den Systemadministrator als solchen', () => {
    // Nur ihn: dass jemand Veranstalter ist, ist der Normalfall und keine
    // Auskunft — es stand als Etikett „veranstalter" auf jedem Bildschirm.
    aufbau({ me: fx.meResponse({ isSystemAdmin: true }) })
    expect(screen.getByText(/Systemadministrator/)).toBeInTheDocument()
  })

  it('sagt bei allen anderen nichts über Rollen', () => {
    aufbau()
    expect(screen.queryByText(/Systemadministrator/)).not.toBeInTheDocument()
  })

  it('kommt zurecht, solange die Auskunft über den Aufrufer noch lädt', () => {
    aufbau({ me: null })
    expect(screen.getByText('1 Turnier')).toBeInTheDocument()
  })

  it('zeigt die Ladeanzeige, solange noch nichts da ist', () => {
    aufbau({ tournaments: [], loading: true })
    expect(screen.getByRole('status')).toHaveTextContent('Turniere werden geladen …')
  })

  it('sagt beim leeren Bestand, was ein Turnier überhaupt braucht', () => {
    aufbau({ tournaments: [], loading: false })

    expect(screen.getByText('Noch kein Turnier ausgeschrieben.')).toBeInTheDocument()
    expect(screen.getByText('Noch kein Turnier')).toBeInTheDocument()
    expect(screen.getByText(/einen Namen, einen Ort und eine Disziplin/)).toBeInTheDocument()
  })

  it('nennt je Turnier Ort, Disziplin, Termin und Zustand', () => {
    aufbau({
      tournaments: [
        fx.tournamentSummary({ discipline: Discipline.Doubles, state: TournamentState.InProgress }),
      ],
    })

    expect(screen.getByText('TC Musterstadt · Doppel')).toBeInTheDocument()
    expect(screen.getByText(/16\..*17\. Mai 2026/)).toBeInTheDocument()
    expect(screen.getByText('läuft')).toBeInTheDocument()
    expect(screen.getByText('4 im Feld')).toBeInTheDocument()
  })

  it('sagt im Entwurf, dass es noch keine Meldungen gibt', () => {
    aufbau({ tournaments: [fx.tournamentSummary({ state: TournamentState.Draft })] })
    expect(screen.getByText('noch keine Meldungen')).toBeInTheDocument()
  })

  it('hebt das gewählte Turnier hervor', () => {
    aufbau({
      tournaments: [
        fx.tournamentSummary(),
        fx.tournamentSummary({ id: fx.IDS.otherTournament, name: 'Herbstturnier' }),
      ],
    })

    const karten = screen
      .getAllByRole('button')
      .filter((knopf) => knopf.textContent?.includes('TC Musterstadt'))
    expect(karten[0]).toHaveAttribute('aria-current', 'true')
    expect(karten[1]).not.toHaveAttribute('aria-current')
  })

  it('wählt beim Öffnen aus und wechselt in den Ablauf', async () => {
    const selectTournament = vi.fn()
    const { onOpen } = aufbau({ selectTournament })

    await user().click(screen.getByText('Clubmeisterschaft 2026'))

    expect(selectTournament).toHaveBeenCalledWith(fx.IDS.tournament)
    expect(onOpen).toHaveBeenCalled()
  })

  it('führt zum Anlegen', async () => {
    const { onCreate } = aufbau()

    await user().click(screen.getByRole('button', { name: 'Turnier anlegen' }))
    expect(onCreate).toHaveBeenCalled()
  })
})

describe('useWorkspace', () => {
  it('verlangt den Arbeitsbereich', () => {
    expect(() => renderHook(() => useWorkspace())).toThrow(
      'useWorkspace muss innerhalb des AppShell stehen.',
    )
  })

  it('lässt ihn der öffentlichen Ansicht fehlen', () => {
    const { result } = renderHook(() => useWorkspaceOptional())
    expect(result.current).toBeNull()
  })
})
