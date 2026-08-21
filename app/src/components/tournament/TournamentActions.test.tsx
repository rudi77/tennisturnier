import { screen, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { TournamentState } from '../../api/types'
import * as fx from '../../test/fixtures'
import { renderWithProviders, user } from '../../test/render'
import { callsTo, server } from '../../test/server'
import { Toast } from '../layout/Toast'
import { TournamentActions } from './TournamentActions'

const T = fx.IDS.tournament

function aufbau(state = TournamentState.DrawGenerated, onDeleted?: () => void) {
  const onChanged = vi.fn(() => Promise.resolve())
  renderWithProviders(
    <>
      <TournamentActions
        tournament={fx.tournamentDetail({ state })}
        onChanged={onChanged}
        onDeleted={onDeleted}
      />
      <Toast />
    </>,
    { workspace: null },
  )
  return onChanged
}

describe('TournamentActions', () => {
  it('bietet den Start nur an, wo ausgelost ist', () => {
    aufbau(TournamentState.DrawGenerated)
    expect(screen.getByRole('button', { name: 'Turnier starten' })).toBeInTheDocument()
  })

  it('bietet ihn nicht an, solange nicht ausgelost ist', () => {
    aufbau(TournamentState.RegistrationOpen)
    expect(screen.queryByRole('button', { name: 'Turnier starten' })).not.toBeInTheDocument()
  })

  it('startet ohne Rückfrage — der Start hat keine Folge, die man bereut', async () => {
    const onChanged = aufbau()

    await user().click(screen.getByRole('button', { name: 'Turnier starten' }))

    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/start`)).toBe(1))
    expect(onChanged).toHaveBeenCalled()
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Turnier gestartet — ab jetzt werden Ergebnisse erfasst',
    )
  })

  it('nennt beim Abbruch die Folge und nicht „sind Sie sicher"', async () => {
    aufbau(TournamentState.InProgress)

    await user().click(screen.getByRole('button', { name: 'Turnier abbrechen' }))

    expect(
      screen.getByText('Abbrechen beendet das Turnier. Gespieltes bleibt lesbar.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ja, turnier abbrechen' })).toBeInTheDocument()
  })

  it('lässt die Rückfrage zurücknehmen', async () => {
    aufbau(TournamentState.InProgress)
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Turnier abbrechen' }))
    await u.click(screen.getByRole('button', { name: 'Zurück' }))

    expect(screen.getByRole('button', { name: 'Turnier abbrechen' })).toBeInTheDocument()
    expect(callsTo('POST', `/api/tournaments/${T}/abandon`)).toBe(0)
  })

  it('bricht ab und sagt, dass Gespieltes lesbar bleibt', async () => {
    const onChanged = aufbau(TournamentState.InProgress)
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Turnier abbrechen' }))
    await u.click(screen.getByRole('button', { name: 'Ja, turnier abbrechen' }))

    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/abandon`)).toBe(1))
    expect(onChanged).toHaveBeenCalled()
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Turnier abgebrochen. Was gespielt wurde, bleibt lesbar.',
    )
  })

  it('bietet den Abbruch nicht mehr an, wo das Turnier vorbei ist', () => {
    aufbau(TournamentState.Completed)
    expect(screen.queryByRole('button', { name: 'Turnier abbrechen' })).not.toBeInTheDocument()

    expect(screen.getByRole('button', { name: 'Turnier löschen' })).toBeInTheDocument()
  })

  it('bietet den Abbruch auch beim abgebrochenen Turnier nicht mehr an', () => {
    aufbau(TournamentState.Abandoned)
    expect(screen.queryByRole('button', { name: 'Turnier abbrechen' })).not.toBeInTheDocument()
  })

  it('nennt beim Löschen, was verloren geht', async () => {
    aufbau()
    await user().click(screen.getByRole('button', { name: 'Turnier löschen' }))

    expect(screen.getByText(/lässt sich nicht rückgängig machen/)).toBeInTheDocument()
  })

  it('geht beim Löschen zuerst weg und lädt dann nach', async () => {
    const reihenfolge: string[] = []
    const onDeleted = vi.fn(() => reihenfolge.push('weg'))
    const onChanged = vi.fn(() => {
      reihenfolge.push('nachgeladen')
      return Promise.resolve()
    })

    renderWithProviders(
      <>
        <TournamentActions
          tournament={fx.tournamentDetail()}
          onChanged={onChanged}
          onDeleted={onDeleted}
        />
        <Toast />
      </>,
      { workspace: null },
    )

    const u = user()
    await u.click(screen.getByRole('button', { name: 'Turnier löschen' }))
    await u.click(screen.getByRole('button', { name: 'Ja, turnier löschen' }))

    await waitFor(() => expect(callsTo('DELETE', `/api/tournaments/${T}`)).toBe(1))
    expect(reihenfolge).toEqual(['weg', 'nachgeladen'])
  })

  it('kommt ohne Weg zurück aus', async () => {
    aufbau(TournamentState.Draft)
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Turnier löschen' }))
    await u.click(screen.getByRole('button', { name: 'Ja, turnier löschen' }))

    await waitFor(() => expect(callsTo('DELETE', `/api/tournaments/${T}`)).toBe(1))
  })

  it('meldet einen abgewiesenen Zug mit seinem Zusammenhang', async () => {
    server.use(
      http.post(`/api/tournaments/${T}/start`, () =>
        HttpResponse.json(
          { detail: 'Ohne Draw lässt sich nicht starten.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )

    aufbau()
    await user().click(screen.getByRole('button', { name: 'Turnier starten' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Start: Ohne Draw lässt sich nicht starten.',
    )
  })

  it('sperrt, solange der Zug läuft', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.post(`/api/tournaments/${T}/start`, async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return new HttpResponse(null, { status: 204 })
      }),
    )

    aufbau()
    await user().click(screen.getByRole('button', { name: 'Turnier starten' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Startet …' })).toBeDisabled())

    freigeben()
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Turnier starten' })).not.toBeDisabled(),
    )
  })

  it('sperrt auch die Rückfrage, solange sie läuft', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.delete(`/api/tournaments/${T}`, async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return new HttpResponse(null, { status: 204 })
      }),
    )

    aufbau()
    const u = user()
    await u.click(screen.getByRole('button', { name: 'Turnier löschen' }))
    await u.click(screen.getByRole('button', { name: 'Ja, turnier löschen' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Läuft …' })).toBeDisabled())

    freigeben()

    // Danach steht wieder die gewöhnliche Zeile da: die Rückfrage ist erledigt.
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Turnier löschen' })).toBeInTheDocument(),
    )
  })
})
