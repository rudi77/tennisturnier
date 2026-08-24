import { screen, waitFor } from '@testing-library/react'
import { http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { EntryStatus, type EntryOverview } from '../../api/types'
import { IDS, entryOverview } from '../../test/fixtures'
import { renderWithProviders, user } from '../../test/render'
import { callsTo, db, lastBody, problem, server } from '../../test/server'
import { Toast } from '../layout/Toast'
import { TeamPanel, openEntriesOf, teamsOf } from './TeamPanel'

const T = IDS.tournament

/** Vier angenommene Einzelmeldungen — der Stand vor der Teambildung. */
const OFFEN: EntryOverview[] = [
  entryOverview({ id: 'e1', participantName: 'Berger, Anna' }),
  entryOverview({ id: 'e2', participantName: 'Huber, Eva' }),
  entryOverview({ id: 'e3', participantName: 'Moser, Lisa' }),
  entryOverview({ id: 'e4', participantName: 'Wagner, Sara' }),
]

/** Zwei Meldungen und das Team, in dem sie stecken. */
const MIT_TEAM: EntryOverview[] = [
  entryOverview({ id: 'e1', participantName: 'Berger, Anna', status: EntryStatus.Paired, teamEntryId: 't1' }),
  entryOverview({ id: 'e2', participantName: 'Huber, Eva', status: EntryStatus.Paired, teamEntryId: 't1' }),
  entryOverview({ id: 't1', participantName: 'Berger, Anna / Huber, Eva' }),
]

function aufbau(entries: EntryOverview[] = OFFEN, disabled = false) {
  const onChanged = vi.fn(() => Promise.resolve())

  renderWithProviders(
    <>
      <TeamPanel tournamentId={T} entries={entries} disabled={disabled} onChanged={onChanged} />
      <Toast />
    </>,
    { workspace: null },
  )

  return onChanged
}

describe('teamsOf und openEntriesOf', () => {
  it('dreht die Zuordnung um: vom Team zu seinen Meldungen', () => {
    const teams = teamsOf(MIT_TEAM)

    expect(teams).toHaveLength(1)
    expect(teams[0]!.entry.id).toBe('t1')
    expect(teams[0]!.members.map((m) => m.id)).toEqual(['e1', 'e2'])
  })

  it('zählt nur angenommene Meldungen ohne Team als offen', () => {
    const entries = [
      ...MIT_TEAM,
      entryOverview({ id: 'e3', status: EntryStatus.Applied }),
      entryOverview({ id: 'e4', status: EntryStatus.WaitingList }),
      entryOverview({ id: 'e5', status: EntryStatus.Accepted }),
    ]

    // Weder das Team selbst noch die beiden darin, weder Gemeldete noch die
    // Warteliste — offen ist, wer im Feld steht und allein dasteht.
    expect(openEntriesOf(entries).map((e) => e.id)).toEqual(['e5'])
  })
})

describe('TeamPanel', () => {
  it('zeigt, wie viele noch ohne Team sind', () => {
    aufbau()

    expect(screen.getByText('4 ohne Team')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Teams auslosen' })).toBeEnabled()
  })

  it('stellt ein Team erst, wenn genau zwei ausgewählt sind', async () => {
    aufbau()
    const knopf = screen.getByRole('button', { name: 'Team stellen' })

    expect(knopf).toBeDisabled()

    await user().click(screen.getByRole('button', { name: 'Berger, Anna' }))
    expect(knopf).toBeDisabled()

    await user().click(screen.getByRole('button', { name: 'Huber, Eva' }))
    expect(knopf).toBeEnabled()

    // Und die Auswahl lässt sich wieder zurücknehmen.
    await user().click(screen.getByRole('button', { name: 'Huber, Eva' }))
    expect(knopf).toBeDisabled()
  })

  it('schiebt bei der dritten Auswahl die älteste heraus', async () => {
    aufbau()

    await user().click(screen.getByRole('button', { name: 'Berger, Anna' }))
    await user().click(screen.getByRole('button', { name: 'Huber, Eva' }))
    await user().click(screen.getByRole('button', { name: 'Moser, Lisa' }))

    expect(screen.getByRole('button', { name: 'Berger, Anna' })).toHaveAttribute(
      'aria-pressed',
      'false',
    )
    expect(screen.getByRole('button', { name: 'Moser, Lisa' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('schickt die beiden Meldungen samt Teamnamen', async () => {
    const onChanged = aufbau()

    await user().click(screen.getByRole('button', { name: 'Berger, Anna' }))
    await user().click(screen.getByRole('button', { name: 'Huber, Eva' }))
    await user().type(screen.getByLabelText('Teamname'), '  Die Unbeugsamen  ')
    await user().click(screen.getByRole('button', { name: 'Team stellen' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/tournaments/${T}/teams`)).toEqual({
        firstEntryId: 'e1',
        secondEntryId: 'e2',
        teamName: 'Die Unbeugsamen',
      }),
    )

    expect(onChanged).toHaveBeenCalled()
    expect(await screen.findByText('Team gestellt')).toBeInTheDocument()
  })

  it('schickt ohne Namen keinen leeren', async () => {
    aufbau()

    await user().click(screen.getByRole('button', { name: 'Berger, Anna' }))
    await user().click(screen.getByRole('button', { name: 'Huber, Eva' }))
    await user().click(screen.getByRole('button', { name: 'Team stellen' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/tournaments/${T}/teams`)).toMatchObject({ teamName: null }),
    )
  })

  it('lost aus und sagt nichts weiter, wenn alle ein Team haben', async () => {
    db.drawTeamsResult = { formed: 2, leftOver: 0 }
    const onChanged = aufbau()

    await user().click(screen.getByRole('button', { name: 'Teams auslosen' }))

    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/teams/draw`)).toBe(1))
    expect(onChanged).toHaveBeenCalled()
    expect(await screen.findByText('Teams ausgelost')).toBeInTheDocument()
  })

  it('sagt es, wenn jemand ohne Partner bleibt', async () => {
    // Sonst stünde die Turnierleitung vor einem Draw, der sie abweist, ohne
    // dass sie wüsste, woran es liegt.
    db.drawTeamsResult = { formed: 1, leftOver: 1 }
    aufbau(OFFEN.slice(0, 3))

    await user().click(screen.getByRole('button', { name: 'Teams auslosen' }))

    expect(
      await screen.findByText('1 Teams — eine Meldung ist ohne Partner geblieben'),
    ).toBeInTheDocument()
  })

  it('zeigt gestellte Teams und löst sie wieder auf', async () => {
    const onChanged = aufbau(MIT_TEAM)

    expect(screen.getByText('Berger, Anna / Huber, Eva')).toBeInTheDocument()
    expect(screen.getByText('Alle Meldungen haben ein Team.')).toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'auflösen' }))

    await waitFor(() => expect(callsTo('DELETE', `/api/tournaments/${T}/teams/t1`)).toBe(1))
    expect(onChanged).toHaveBeenCalled()
  })

  it('sagt beim leeren Feld, dass es erst Meldungen braucht', () => {
    aufbau([])

    expect(
      screen.getByText('Noch keine angenommene Meldung — Teams lassen sich erst danach stellen.'),
    ).toBeInTheDocument()
  })

  it('lost nicht aus, solange nur eine Meldung offen ist', () => {
    aufbau(OFFEN.slice(0, 1))

    expect(screen.getByRole('button', { name: 'Teams auslosen' })).toBeDisabled()
  })

  it('rührt nach der Auslosung nichts mehr an', () => {
    aufbau(MIT_TEAM, true)

    expect(screen.getByRole('button', { name: 'auflösen' })).toBeDisabled()
  })

  it('meldet einen Fehler, statt ihn zu verschlucken', async () => {
    // Die Meldung, die es nicht mehr gibt: der zweite Browserreiter hat sie
    // eben aufgelöst.
    server.use(
      http.post(`/api/tournaments/${T}/teams`, () =>
        problem(404, 'Meldung wurde nicht gefunden.', 'Nicht gefunden'),
      ),
    )

    aufbau(OFFEN)

    await user().click(screen.getByRole('button', { name: 'Berger, Anna' }))
    await user().click(screen.getByRole('button', { name: 'Huber, Eva' }))
    await user().click(screen.getByRole('button', { name: 'Team stellen' }))

    // Die Oberfläche übersetzt den 404 in ihre eigene Auskunft — für den
    // Melder ist „gibt es nicht" dasselbe wie „darfst du nicht" (ADR-0004).
    expect(await screen.findByText(/Team gestellt: Nicht gefunden/)).toBeInTheDocument()
  })
})
