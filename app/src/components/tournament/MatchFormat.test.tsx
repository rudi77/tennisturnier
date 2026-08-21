import { fireEvent, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { FinalSetMode, TournamentState, type MatchFormat } from '../../api/types'
import * as fx from '../../test/fixtures'
import { renderWithProviders, user } from '../../test/render'
import { db, lastBody } from '../../test/server'
import { MatchFormatPanel } from './MatchFormatPanel'
import { MatchFormatPicker } from './MatchFormatPicker'

const STANDARD: MatchFormat = {
  bestOf: 3,
  finalSetMode: FinalSetMode.MatchTiebreak10,
  tiebreakAt: 6,
}

describe('MatchFormatPicker', () => {
  function aufbau(value: MatchFormat = STANDARD, disabled = false) {
    const onChange = vi.fn()
    renderWithProviders(<MatchFormatPicker value={value} onChange={onChange} disabled={disabled} />, {
      workspace: null,
    })
    return onChange
  }

  it('zeigt, was eingestellt ist', () => {
    aufbau()

    expect(screen.getByRole('button', { name: '2 Gewinnsätze' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'bis 6' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Champions-Tiebreak' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('stellt die Satzzahl um', async () => {
    const onChange = aufbau()
    await user().click(screen.getByRole('button', { name: '3 Gewinnsätze' }))
    expect(onChange).toHaveBeenCalledWith({ ...STANDARD, bestOf: 5 })
  })

  it('stellt die Satzlänge über die Abkürzung um', async () => {
    const onChange = aufbau()
    await user().click(screen.getByRole('button', { name: 'bis 4' }))
    expect(onChange).toHaveBeenCalledWith({ ...STANDARD, tiebreakAt: 4 })
  })

  it('lässt jede Länge zu, die auch die Domäne zulässt', () => {
    const onChange = aufbau()
    const feld = screen.getByLabelText('Spiele pro Satz')

    fireEvent.change(feld, { target: { value: '9' } })

    expect(onChange).toHaveBeenLastCalledWith({ ...STANDARD, tiebreakAt: 9 })
  })

  it('hält die Länge in den Grenzen der Domäne', () => {
    const onChange = aufbau()
    const feld = screen.getByLabelText('Spiele pro Satz')

    fireEvent.change(feld, { target: { value: '40' } })
    expect(onChange).toHaveBeenLastCalledWith({ ...STANDARD, tiebreakAt: 12 })

    fireEvent.change(feld, { target: { value: '0' } })
    expect(onChange).toHaveBeenLastCalledWith({ ...STANDARD, tiebreakAt: 1 })

    fireEvent.change(feld, { target: { value: '6.4' } })
    expect(onChange).toHaveBeenLastCalledWith({ ...STANDARD, tiebreakAt: 6 })

    // Ein Zahlenfeld gibt bei Unsinn eine leere Zeichenkette heraus; sie wird
    // auf die Untergrenze gehoben statt als Fehler behandelt.
    fireEvent.change(feld, { target: { value: '' } })
    expect(onChange).toHaveBeenLastCalledWith({ ...STANDARD, tiebreakAt: 1 })
  })

  it('stellt den Entscheidungssatz um', async () => {
    const onChange = aufbau()
    await user().click(screen.getByRole('button', { name: 'Vorteilssatz' }))
    expect(onChange).toHaveBeenCalledWith({ ...STANDARD, finalSetMode: FinalSetMode.Advantage })

    await user().click(screen.getByRole('button', { name: 'wie jeder Satz' }))
    expect(onChange).toHaveBeenCalledWith({ ...STANDARD, finalSetMode: FinalSetMode.Regular })
  })

  it('sagt, welcher Satz der entscheidende ist', () => {
    const { unmount } = renderWithProviders(
      <MatchFormatPicker value={STANDARD} onChange={vi.fn()} />,
      { workspace: null },
    )
    expect(screen.getByText('Nur der 3. Satz — die davor werden normal gespielt.')).toBeInTheDocument()
    unmount()

    renderWithProviders(
      <MatchFormatPicker value={{ ...STANDARD, bestOf: 1 }} onChange={vi.fn()} />,
      { workspace: null },
    )
    expect(screen.getByText('Bei einem einzigen Satz ist er das ganze Match.')).toBeInTheDocument()
  })

  it('sagt in einem Satz, was daraus folgt', () => {
    aufbau()
    expect(screen.getByText('2 Gewinnsätze bis 6, Champions-Tiebreak statt des letzten')).toBeInTheDocument()
  })

  it('lässt sich abschalten', () => {
    aufbau(STANDARD, true)

    for (const knopf of screen.getAllByRole('button')) expect(knopf).toBeDisabled()
    expect(screen.getByLabelText('Spiele pro Satz')).toBeDisabled()
  })
})

describe('MatchFormatPanel', () => {
  function aufbau(over: Parameters<typeof fx.tournamentDetail>[0] = {}) {
    const onChanged = vi.fn(() => Promise.resolve())
    const tournament = fx.tournamentDetail(over)
    renderWithProviders(<MatchFormatPanel tournament={tournament} onChanged={onChanged} />, {
      workspace: null,
    })
    return { onChanged, tournament }
  }

  it('nennt das geltende Format in einem Satz', () => {
    aufbau()
    expect(screen.getByText('2 Gewinnsätze bis 6, Champions-Tiebreak statt des letzten')).toBeInTheDocument()
  })

  it('öffnet und schließt die Einstellung', async () => {
    aufbau()
    const u = user()

    expect(screen.queryByText('Spiele pro Satz')).not.toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Ändern' }))
    expect(screen.getByLabelText('Spiele pro Satz')).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Schließen' }))
    expect(screen.queryByLabelText('Spiele pro Satz')).not.toBeInTheDocument()
  })

  it('übernimmt erst, wenn sich etwas geändert hat', async () => {
    const { onChanged, tournament } = aufbau()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Ändern' }))
    expect(screen.getByRole('button', { name: 'Übernehmen' })).toBeDisabled()

    await u.click(screen.getByRole('button', { name: 'bis 4' }))
    await u.click(screen.getByRole('button', { name: 'Übernehmen' }))

    await waitFor(() =>
      expect(lastBody('PUT', `/api/tournaments/${tournament.id}/match-format`)).toEqual({
        matchFormat: { bestOf: 3, finalSetMode: FinalSetMode.MatchTiebreak10, tiebreakAt: 4 },
      }),
    )
    expect(onChanged).toHaveBeenCalled()
  })

  it('bietet die Rückkehr zur Vorlage nur an, wo etwas eingestellt ist', async () => {
    aufbau()
    await user().click(screen.getByRole('button', { name: 'Ändern' }))
    expect(screen.queryByRole('button', { name: 'Zurück zur Vorlage' })).not.toBeInTheDocument()
  })

  it('nimmt eine eigene Einstellung zurück', async () => {
    const { tournament } = aufbau({
      matchFormat: { bestOf: 1, finalSetMode: FinalSetMode.Regular, tiebreakAt: 4 },
    })
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Ändern' }))
    await u.click(screen.getByRole('button', { name: 'Zurück zur Vorlage' }))

    await waitFor(() =>
      expect(lastBody('PUT', `/api/tournaments/${tournament.id}/match-format`)).toEqual({
        matchFormat: null,
      }),
    )
  })

  it('ist ab der Auslosung eingefroren und sagt auch warum', () => {
    aufbau({ state: TournamentState.DrawGenerated })

    expect(screen.queryByRole('button', { name: 'Ändern' })).not.toBeInTheDocument()
    expect(screen.getByText(/Mit der Auslosung eingefroren/)).toBeInTheDocument()
  })

  it('bleibt auch nach dem Turnier eingefroren', () => {
    aufbau({ state: TournamentState.Completed })
    expect(screen.queryByRole('button', { name: 'Ändern' })).not.toBeInTheDocument()
  })

  it('folgt dem Turnier, wenn sich das Format von anderswo ändert', async () => {
    const onChanged = vi.fn(() => Promise.resolve())
    const { rerender } = renderWithProviders(
      <MatchFormatPanel tournament={fx.tournamentDetail()} onChanged={onChanged} />,
      { workspace: null },
    )

    await user().click(screen.getByRole('button', { name: 'Ändern' }))

    rerender(
      <MatchFormatPanel
        tournament={fx.tournamentDetail({
          effectiveMatchFormat: { bestOf: 1, finalSetMode: FinalSetMode.Regular, tiebreakAt: 4 },
        })}
        onChanged={onChanged}
      />,
    )

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'ein Satz' })).toHaveAttribute('aria-pressed', 'true'),
    )
    expect(screen.getByRole('button', { name: 'Übernehmen' })).toBeDisabled()
  })

  it('meldet einen abgewiesenen Wechsel', async () => {
    db.tournament = fx.tournamentDetail()
    const { tournament } = aufbau()
    const u = user()

    const { server } = await import('../../test/server')
    const { http, HttpResponse } = await import('msw')
    server.use(
      http.put(`/api/tournaments/${tournament.id}/match-format`, () =>
        HttpResponse.json(
          { detail: 'Das Format ist eingefroren.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )

    await u.click(screen.getByRole('button', { name: 'Ändern' }))
    await u.click(screen.getByRole('button', { name: 'bis 8' }))
    await u.click(screen.getByRole('button', { name: 'Übernehmen' }))

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Übernehmen' })).not.toBeDisabled(),
    )
  })
})
