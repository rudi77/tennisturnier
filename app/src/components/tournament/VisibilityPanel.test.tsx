/**
 * Wer außer den Mitgliedern zusehen darf.
 *
 * Ein Turnier ist zuerst eine Gruppe (ADR-0012). Der Aushang im Vereinsheim
 * bleibt möglich — er ist eine Entscheidung geworden, und der Zuschauerlink
 * steht erst da, wenn er auch trägt.
 */

import { screen, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import * as fx from '../../test/fixtures'
import { lastBody, server } from '../../test/server'
import { renderWithProviders, user } from '../../test/render'
import { Toast } from '../layout/Toast'
import { VisibilityPanel } from './VisibilityPanel'

const T = fx.IDS.tournament

function aufbau(isPublic = false) {
  const onChanged = vi.fn()

  renderWithProviders(
    <>
      <VisibilityPanel tournament={fx.tournamentDetail({ isPublic })} onChanged={onChanged} />
      <Toast />
    </>,
    { workspace: null },
  )

  return onChanged
}

describe('VisibilityPanel', () => {
  it('zeigt privat als den geltenden Zustand', () => {
    aufbau()

    expect(screen.getByRole('button', { name: 'Privat' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Öffentlich' })).toHaveAttribute(
      'aria-pressed',
      'false',
    )
  })

  it('verschweigt den Zuschauerlink, solange er nicht trägt', () => {
    // Ein Link, den man kopieren kann und der beim Empfänger auf einen Hinweis
    // führt, wäre schlimmer als keiner.
    aufbau()

    expect(screen.queryByLabelText('Zuschauerlink')).not.toBeInTheDocument()
  })

  it('öffnet das Turnier und sagt, was das heißt', async () => {
    const onChanged = aufbau()

    await user().click(screen.getByRole('button', { name: 'Öffentlich' }))

    await waitFor(() =>
      expect(lastBody('PUT', `/api/tournaments/${T}/visibility`)).toEqual({ isPublic: true }),
    )
    expect(onChanged).toHaveBeenCalled()
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Öffentlich — jeder mit dem Zuschauerlink sieht Spielplan und Ergebnisse',
    )
  })

  it('zeigt den Zuschauerlink, sobald er trägt', () => {
    aufbau(true)

    expect(screen.getByLabelText('Zuschauerlink')).toHaveValue(
      `${window.location.origin}/?t=${T}`,
    )
  })

  it('markiert den Link beim Fokussieren — abtippen geht immer', async () => {
    aufbau(true)
    const feld = screen.getByLabelText('Zuschauerlink') as HTMLInputElement

    await user().click(feld)

    expect(feld.selectionStart).toBe(0)
    expect(feld.selectionEnd).toBe(feld.value.length)
  })

  it('schließt es wieder — das ist der wichtigere Weg', async () => {
    aufbau(true)

    await user().click(screen.getByRole('button', { name: 'Privat' }))

    await waitFor(() =>
      expect(lastBody('PUT', `/api/tournaments/${T}/visibility`)).toEqual({ isPublic: false }),
    )
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Privat — nur noch Mitglieder sehen das Turnier',
    )
  })

  it('meldet einen abgewiesenen Wechsel', async () => {
    server.use(
      http.put(`/api/tournaments/${T}/visibility`, () =>
        HttpResponse.json(
          { detail: 'Das darfst du nicht.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()

    await user().click(screen.getByRole('button', { name: 'Öffentlich' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Sichtbarkeit: Das darfst du nicht.',
    )
  })
})
