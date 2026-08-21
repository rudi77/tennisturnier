import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ProposalChange, ScheduleConstraint } from '../../api/types'
import * as fx from '../../test/fixtures'
import { user } from '../../test/render'
import { ProposalBanner } from './ProposalBanner'

function aufbau(over: Parameters<typeof fx.schedulePlan>[0] = {}, busy = false) {
  const onConfirm = vi.fn()
  const onDiscard = vi.fn()
  const result = render(
    <ProposalBanner
      proposal={fx.schedulePlan(over)}
      timeZone="Europe/Vienna"
      busy={busy}
      onConfirm={onConfirm}
      onDiscard={onDiscard}
    />,
  )
  return { onConfirm, onDiscard, ...result }
}

describe('ProposalBanner', () => {
  it('zeigt den Diff, nicht das Ergebnis', () => {
    aufbau()
    expect(screen.getByText(/1 verschoben · 1 neu · 0 unverändert/)).toBeInTheDocument()
  })

  it('nennt Entfallenes nur, wo etwas entfällt', () => {
    const { unmount } = aufbau()
    expect(screen.queryByText(/entfallen/)).not.toBeInTheDocument()
    unmount()

    aufbau({ diff: { unchanged: 2, added: 0, moved: 0, removed: 3 } })
    expect(screen.getByText(/3 entfallen/)).toBeInTheDocument()
  })

  it('sagt ausdrücklich, dass noch nichts eingetragen ist', () => {
    aufbau()
    expect(screen.getByText(/Nichts davon ist eingetragen, solange nicht übernommen wird/)).toBeInTheDocument()
  })

  it('nennt jeden Verstoß im Klartext', () => {
    aufbau({
      violations: [
        {
          constraint: ScheduleConstraint.PlayerDoubleBooked,
          message: 'S. Moser um 14:00 zweimal',
          assignmentId: fx.IDS.assignment1,
        },
      ],
    })

    expect(screen.getByText('Verstöße')).toBeInTheDocument()
    expect(screen.getByText('Spieler doppelt angesetzt')).toBeInTheDocument()
    expect(screen.getByText(/S\. Moser um 14:00 zweimal/)).toBeInTheDocument()
  })

  it('schweigt über Verstöße, wo es keine gibt', () => {
    aufbau({ violations: [] })
    expect(screen.queryByText('Verstöße')).not.toBeInTheDocument()
  })

  it('nennt, was ohne Platz geblieben ist, und warum', () => {
    aufbau()
    expect(screen.getByText('Ohne Platz geblieben')).toBeInTheDocument()
    expect(screen.getByText(/Teilnehmer stehen noch nicht fest/)).toBeInTheDocument()
  })

  it('nimmt für ein Match ohne Bezeichner den Anfang seiner Kennung', () => {
    aufbau({
      unscheduled: [{ matchId: fx.IDS.match3, label: null, reason: 'kein Platz frei' }],
    })
    expect(screen.getByText(fx.IDS.match3.slice(0, 8))).toBeInTheDocument()
  })

  it('schweigt über Unplatziertes, wo alles einen Platz hat', () => {
    aufbau({ unscheduled: [] })
    expect(screen.queryByText('Ohne Platz geblieben')).not.toBeInTheDocument()
  })

  it('übernimmt und verwirft auf Zuruf', async () => {
    const { onConfirm, onDiscard } = aufbau()
    const u = user()

    await u.click(screen.getByRole('button', { name: 'Übernehmen' }))
    expect(onConfirm).toHaveBeenCalled()

    await u.click(screen.getByRole('button', { name: 'Verwerfen' }))
    expect(onDiscard).toHaveBeenCalled()
  })

  it('sperrt beides, solange übernommen wird', () => {
    aufbau({}, true)

    expect(screen.getByRole('button', { name: 'Übernimmt …' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Verwerfen' })).toBeDisabled()
  })

  it('lässt einen leeren Vorschlag nicht übernehmen', () => {
    aufbau({ assignments: [] })
    expect(screen.getByRole('button', { name: 'Übernehmen' })).toBeDisabled()
  })

  it('zeigt zu einem leeren Vorschlag auch keine Begründungen an', () => {
    aufbau({ assignments: [] })
    expect(screen.queryByText(/Begründungen/)).not.toBeInTheDocument()
  })

  it('zeigt die Begründung je Ansetzung — ohne sie wird die Automatik umgangen', async () => {
    aufbau()
    const u = user()

    const knopf = screen.getByRole('button', { name: 'Begründungen anzeigen (2)' })
    expect(knopf).toHaveAttribute('aria-expanded', 'false')

    await u.click(knopf)

    expect(screen.getByText('frühestmöglich')).toBeInTheDocument()
    expect(
      screen.getByText('nach dem Vorspiel, zuzüglich 30 Minuten Pause'),
    ).toBeInTheDocument()
    expect(screen.getByText('Platz 1 · 10:00')).toBeInTheDocument()
    expect(screen.getByText('neu')).toBeInTheDocument()
    expect(screen.getByText('verschoben')).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: 'Begründungen ausblenden' }))
    expect(screen.queryByText('frühestmöglich')).not.toBeInTheDocument()
  })

  it('nimmt in der Begründungsliste den Anfang der Kennung, wo kein Bezeichner steht', async () => {
    aufbau({
      assignments: [
        {
          matchId: fx.IDS.match1,
          label: null,
          courtId: fx.IDS.court1,
          courtName: 'Platz 1',
          sequenceOnCourt: 1,
          plannedStart: '2026-05-16T08:00:00+00:00',
          plannedEnd: '2026-05-16T09:00:00+00:00',
          estimatedDuration: '01:00:00',
          change: ProposalChange.Unchanged,
          reason: 'bleibt stehen',
        },
      ],
    })

    await user().click(screen.getByRole('button', { name: 'Begründungen anzeigen (1)' }))

    expect(screen.getByText(fx.IDS.match1.slice(0, 6))).toBeInTheDocument()
    expect(screen.getByText('unverändert')).toBeInTheDocument()
  })
})
