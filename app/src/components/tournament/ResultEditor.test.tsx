import { screen, waitFor } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { FinalSetMode, MatchOutcome, type MatchDetail, type MatchFormat } from '../../api/types'
import * as fx from '../../test/fixtures'
import { renderWithProviders, user } from '../../test/render'
import { lastBody, server } from '../../test/server'
import { Toast } from '../layout/Toast'
import { ResultEditor } from './ResultEditor'

const ZWEI_GEWINNSAETZE: MatchFormat = {
  bestOf: 3,
  finalSetMode: FinalSetMode.MatchTiebreak10,
  tiebreakAt: 6,
}

function aufbau(
  over: {
    match?: MatchDetail
    format?: MatchFormat
    nextRoundName?: string | null
  } = {},
) {
  const onClose = vi.fn()
  const onSaved = vi.fn(() => Promise.resolve())

  renderWithProviders(
    <>
      <ResultEditor
        match={over.match ?? fx.match()}
        matchLabel="M1"
        meta="Hauptfeld · Runde 1"
        format={over.format ?? ZWEI_GEWINNSAETZE}
        nextRoundName={over.nextRoundName === undefined ? 'Halbfinale' : over.nextRoundName}
        onClose={onClose}
        onSaved={onSaved}
      />
      <Toast />
    </>,
    { workspace: null },
  )

  return { onClose, onSaved }
}

/** Einen Satzstand über den Stepper hochzählen. */
async function zaehle(label: string, mal: number): Promise<void> {
  const u = user()
  const knopf = screen.getByRole('button', { name: `${label} erhöhen` })
  for (let i = 0; i < mal; i++) await u.click(knopf)
}

describe('ResultEditor', () => {
  it('nennt Match, Runde und die beiden Seiten', () => {
    aufbau()

    expect(screen.getByRole('dialog', { name: 'Ergebnis erfassen' })).toBeInTheDocument()
    expect(screen.getByText('M1')).toBeInTheDocument()
    expect(screen.getByText('Hauptfeld · Runde 1')).toBeInTheDocument()
    expect(screen.getByTitle('S. Moser')).toBeInTheDocument()
    expect(screen.getByTitle('L. Berger')).toBeInTheDocument()
  })

  it('zeigt zunächst nur den ersten Satz', () => {
    aufbau()

    expect(screen.getByText('Satz 1')).toBeInTheDocument()
    expect(screen.queryByText('Satz 2')).not.toBeInTheDocument()
  })

  it('gibt den nächsten Satz frei, sobald der vorige gespielt ist', async () => {
    aufbau()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 4)

    expect(await screen.findByText('Satz 2')).toBeInTheDocument()
  })

  it('hört beim entscheidenden Satz auf — die Domäne weist einen weiteren ab', async () => {
    aufbau()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 4)
    await zaehle('S. Moser, Satz 2', 6)
    await zaehle('L. Berger, Satz 2', 3)

    expect(screen.queryByText('M-Tiebreak')).not.toBeInTheDocument()
  })

  it('nennt den Match-Tiebreak beim Namen und lässt ihn über 10 laufen', async () => {
    aufbau()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 4)
    await zaehle('L. Berger, Satz 2', 6)
    await zaehle('S. Moser, Satz 2', 3)

    expect(await screen.findByText('M-Tiebreak')).toBeInTheDocument()
    expect(
      screen.getByText('Der Entscheidungssatz ist ein Match-Tiebreak: bis 10, mit zwei Punkten Vorsprung.'),
    ).toBeInTheDocument()

    await zaehle('S. Moser, M-Tiebreak', 12)
    expect(screen.getByLabelText('S. Moser, M-Tiebreak')).toHaveTextContent('12')
  })

  it('sagt vorher, warum sich noch nichts speichern lässt', async () => {
    aufbau()

    expect(screen.getByRole('status')).toHaveTextContent('Noch kein Satz eingetragen.')
    expect(screen.getByRole('button', { name: 'Speichern & propagieren' })).toBeDisabled()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 4)

    expect(await screen.findByText('Noch nicht entschieden — es fehlt ein Satz (Stand 1:0).')).toBeInTheDocument()
  })

  it('speichert ein entschiedenes Match und meldet, was daraus folgt', async () => {
    const { onClose, onSaved } = aufbau()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 4)
    await zaehle('S. Moser, Satz 2', 6)
    await zaehle('L. Berger, Satz 2', 3)

    await user().click(screen.getByRole('button', { name: 'Speichern & propagieren' }))

    await waitFor(() =>
      expect(lastBody('PUT', `/api/matches/${fx.IDS.match1}/result`)).toEqual({
        outcome: MatchOutcome.Normal,
        sets: [
          { games1: 6, games2: 4, tiebreakPoints: null },
          { games1: 6, games2: 3, tiebreakPoints: null },
        ],
        abandonedSet: null,
        affectedSide: null,
      }),
    )

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Ergebnis gespeichert · M1 — Refs in Halbfinale aufgelöst, Projektion neu gebaut',
    )
    expect(onSaved).toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })

  it('sagt in der letzten Runde, dass das Turnier damit endet', async () => {
    aufbau({ nextRoundName: null })

    expect(
      screen.getByText('Letzte Runde — mit dem Ergebnis wechselt das Turnier nach Completed.'),
    ).toBeInTheDocument()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 4)
    await zaehle('S. Moser, Satz 2', 6)
    await zaehle('L. Berger, Satz 2', 3)

    await user().click(screen.getByRole('button', { name: 'Speichern & propagieren' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Ergebnis gespeichert · M1')
  })

  it('verlangt beim Nichtantreten die betroffene Seite und schickt keinen Spielstand', async () => {
    aufbau()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Walkover' }))

    expect(screen.getByText('Betroffene Seite')).toBeInTheDocument()
    expect(screen.getByText('Wer nicht angetreten ist beziehungsweise disqualifiziert wurde.')).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'L. Berger', selector: '.md-pill' }))
    await u.click(screen.getByRole('button', { name: 'Speichern & propagieren' }))

    await waitFor(() =>
      expect(lastBody('PUT', `/api/matches/${fx.IDS.match1}/result`)).toEqual({
        outcome: MatchOutcome.Walkover,
        sets: null,
        abandonedSet: null,
        affectedSide: 2,
      }),
    )
  })

  it('sperrt beim Nichtantreten die Satzeingabe', async () => {
    aufbau()
    await user().click(screen.getByRole('button', { name: 'Walkover' }))

    const feld = screen.getByLabelText('S. Moser, Satz 1').closest('div[style*="pointer-events"]')
    expect(feld).toHaveStyle({ pointerEvents: 'none' })
  })

  it('nimmt bei einer Aufgabe den unvollständigen Stand mit', async () => {
    aufbau()
    const u = user()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 2)

    await u.click(screen.getByRole('button', { name: 'Retirement' }))
    expect(screen.getByText('Wer aufgegeben hat — die andere Seite kommt weiter.')).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Speichern & propagieren' }))

    await waitFor(() =>
      expect(lastBody('PUT', `/api/matches/${fx.IDS.match1}/result`)).toEqual({
        outcome: MatchOutcome.Retirement,
        sets: [{ games1: 6, games2: 2, tiebreakPoints: null }],
        abandonedSet: null,
        affectedSide: 1,
      }),
    )
  })

  it('lässt einen anderen Ausgang ohne vollständigen Satzstand speichern', async () => {
    aufbau()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Disqualification' }))
    expect(screen.queryByText(/Noch kein Satz eingetragen/)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Speichern & propagieren' })).not.toBeDisabled()
  })

  it('kehrt zum normalen Ausgang zurück', async () => {
    aufbau()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Walkover' }))
    await u.click(screen.getByRole('button', { name: 'Normal' }))

    expect(screen.queryByText('Betroffene Seite')).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Noch kein Satz eingetragen.')
  })

  it('übernimmt einen bereits eingetragenen Stand zur Korrektur', () => {
    aufbau({
      match: fx.match({
        score: {
          outcome: MatchOutcome.Retirement,
          winnerSide: 1,
          completedSets: [{ games1: 6, games2: 4, tiebreakPoints: null }],
          abandonedSet: null,
          display: '6:4 ret.',
        },
      }),
    })

    expect(screen.getByLabelText('S. Moser, Satz 1')).toHaveTextContent('6')
    expect(screen.getByLabelText('L. Berger, Satz 1')).toHaveTextContent('4')
    expect(screen.getByRole('button', { name: 'Retirement' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('lässt einen Stand, der mehr Sätze trägt als das Format kennt, abgeschnitten', () => {
    aufbau({
      format: { bestOf: 1, finalSetMode: FinalSetMode.Regular, tiebreakAt: 4 },
      match: fx.match({
        score: {
          outcome: MatchOutcome.Normal,
          winnerSide: 1,
          completedSets: [
            { games1: 4, games2: 2, tiebreakPoints: null },
            { games1: 4, games2: 1, tiebreakPoints: null },
          ],
          abandonedSet: null,
          display: '4:2 4:1',
        },
      }),
    })

    expect(screen.getByLabelText('S. Moser, Satz 1')).toHaveTextContent('4')
    expect(screen.queryByText('Satz 2')).not.toBeInTheDocument()
  })

  it('geht ohne Speichern zurück', async () => {
    const { onClose, onSaved } = aufbau()

    await user().click(screen.getByRole('button', { name: 'Abbrechen' }))

    expect(onClose).toHaveBeenCalled()
    expect(onSaved).not.toHaveBeenCalled()
  })

  it('meldet ein abgewiesenes Ergebnis und bleibt offen', async () => {
    server.use(
      http.put(`/api/matches/${fx.IDS.match1}/result`, () =>
        HttpResponse.json(
          { detail: 'Das Match war nach Satz 2 entschieden.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    const { onClose } = aufbau()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 4)
    await zaehle('S. Moser, Satz 2', 6)
    await zaehle('L. Berger, Satz 2', 3)

    await user().click(screen.getByRole('button', { name: 'Speichern & propagieren' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Ergebnis: Das Match war nach Satz 2 entschieden.',
    )
    expect(onClose).not.toHaveBeenCalled()
  })

  it('sperrt, solange gespeichert wird', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.put(`/api/matches/${fx.IDS.match1}/result`, async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { onClose } = aufbau()

    await zaehle('S. Moser, Satz 1', 6)
    await zaehle('L. Berger, Satz 1', 4)
    await zaehle('S. Moser, Satz 2', 6)
    await zaehle('L. Berger, Satz 2', 3)

    await user().click(screen.getByRole('button', { name: 'Speichern & propagieren' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Speichert …' })).toBeDisabled())

    freigeben()
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })
})
