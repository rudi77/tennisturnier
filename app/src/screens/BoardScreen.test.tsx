import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  AssignmentStatus,
  FinalSetMode,
  MatchStatus,
  PhaseFormatKind,
  SchedulingMode,
  ScheduleConstraint,
  TournamentState,
} from '../api/types'
import * as fx from '../test/fixtures'
import { renderWithProviders, user, workspace } from '../test/render'
import { callsTo, db, lastBody, server } from '../test/server'
import { Toast } from '../components/layout/Toast'
import { BoardScreen } from './BoardScreen'

const T = fx.IDS.tournament

function aufbau(over: Parameters<typeof fx.tournamentDetail>[0] | null = {}) {
  const reloadTournament = vi.fn(() => Promise.resolve())
  renderWithProviders(
    <>
      <BoardScreen />
      <Toast />
    </>,
    {
      workspace: workspace({
        tournament:
          over === null
            ? null
            : fx.tournamentDetail({ state: TournamentState.InProgress, ...over }),
        reloadTournament,
      }),
    },
  )
  return { reloadTournament }
}

/** Ein Match mit Zuweisung auf Platz 1 am 16. Mai. */
function angesetzt(over: Parameters<typeof fx.assignment>[0] = {}) {
  return fx.match({
    assignment: fx.assignment({ plannedStart: '2026-05-16T08:00:00+00:00', ...over }),
  })
}

beforeEach(() => {
  window.print = vi.fn()
})

describe('BoardScreen — Rahmen', () => {
  it('sagt ohne Turnier, dass es keinen Spielplan gibt', () => {
    aufbau(null)

    // Einmal im Kopf des Bildschirms, einmal als leerer Zustand.
    expect(screen.getAllByText('Kein Turnier ausgewählt')).toHaveLength(2)
    expect(screen.getByText(/Ohne Turnier gibt es keinen Spielplan/)).toBeInTheDocument()
  })

  it('sagt vor der Auslosung, dass es noch keine Matches gibt', () => {
    aufbau({ state: TournamentState.RegistrationClosed })

    expect(screen.getByText('Noch kein Draw')).toBeInTheDocument()
    expect(callsTo('GET', `/api/tournaments/${T}/phases`)).toBe(0)
  })

  it('nennt Turnier, Plätze und Zeitzone', async () => {
    aufbau()

    expect(await screen.findByText('2 Plätze · Europe/Vienna')).toBeInTheDocument()
  })

  it('teilt alle Matches auf läuft, offen und fertig auf', async () => {
    db.phases = [
      fx.phase({
        matches: [
          fx.match({ status: MatchStatus.Finished, assignment: fx.assignment({ status: AssignmentStatus.Running }) }),
          angesetzt({ status: AssignmentStatus.Running }),
          fx.match({ id: fx.IDS.match3, status: MatchStatus.Pending }),
        ],
      }),
    ]
    aufbau()

    await waitFor(() =>
      expect([...document.querySelectorAll('.md-kpi')].map((el) => el.textContent)).toEqual([
        '1läuft',
        '1offen',
        '1fertig',
      ]),
    )
  })

  it('meldet einen Fehler und bietet einen zweiten Anlauf', async () => {
    server.use(http.get('/api/tournaments/:id/phases', () => new HttpResponse(null, { status: 503 })))
    aufbau()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
  })

  it('zeigt die Ladeanzeige, solange nichts da ist', () => {
    aufbau()
    expect(screen.getByRole('status')).toHaveTextContent('Spielplan wird geladen …')
  })

  it('druckt den Aushang', async () => {
    aufbau()
    await screen.findByText('Aushang drucken')

    await user().click(screen.getByRole('button', { name: 'Aushang drucken' }))
    expect(window.print).toHaveBeenCalled()
  })
})

describe('BoardScreen — Moduswechsel', () => {
  it('ist ein Zustandsübergang und kein stiller Schalter', async () => {
    const { reloadTournament } = aufbau()
    await screen.findByText(/Planungsmodus: Zeitraster/)

    await user().click(screen.getByRole('button', { name: 'Turniertag' }))

    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/scheduling/match-day`)).toBe(1))
    expect(reloadTournament).toHaveBeenCalled()
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Turniertagmodus aktiv — ab jetzt zählt die Reihenfolge auf dem Platz, nicht das Zeitraster',
    )
  })

  it('führt zurück in den Planungsmodus', async () => {
    aufbau({ schedulingMode: SchedulingMode.MatchDay })
    await screen.findByText(/Turniertag: kein Zeitraster/)

    await user().click(screen.getByRole('button', { name: 'Planungsmodus' }))

    await waitFor(() => expect(callsTo('POST', `/api/tournaments/${T}/scheduling/planning`)).toBe(1))
    expect(await screen.findByRole('status')).toHaveTextContent('Planungsmodus aktiv')
  })

  it('tut nichts, wo der Modus schon gilt', async () => {
    aufbau()
    await screen.findByText(/Planungsmodus: Zeitraster/)

    await user().click(screen.getByRole('button', { name: 'Planungsmodus' }))

    expect(callsTo('POST', `/api/tournaments/${T}/scheduling/planning`)).toBe(0)
  })

  it('meldet einen abgewiesenen Wechsel', async () => {
    server.use(
      http.post(`/api/tournaments/${T}/scheduling/match-day`, () =>
        HttpResponse.json(
          { detail: 'Erst ab dem Draw.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByText(/Planungsmodus: Zeitraster/)

    await user().click(screen.getByRole('button', { name: 'Turniertag' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Moduswechsel: Erst ab dem Draw.')
  })
})

describe('BoardScreen — Solver', () => {
  it('rechnet einen Vorschlag, ohne ihn einzutragen', async () => {
    aufbau()
    await screen.findByText('Auto-Plan berechnen')

    await user().click(screen.getByRole('button', { name: 'Auto-Plan berechnen' }))

    expect(await screen.findByText(/ScheduleProposal · Diff/)).toBeInTheDocument()
    expect(callsTo('POST', `/api/tournaments/${T}/schedule/confirm`)).toBe(0)
  })

  it('sagt es, wenn es nichts anzusetzen gibt', async () => {
    db.plan = fx.schedulePlan({ assignments: [] })
    aufbau()
    await screen.findByText('Auto-Plan berechnen')

    await user().click(screen.getByRole('button', { name: 'Auto-Plan berechnen' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Kein Vorschlag — es gibt nichts anzusetzen, was nicht schon läge',
    )
  })

  it('übernimmt den Vorschlag genau so, wie er dasteht', async () => {
    aufbau()
    await screen.findByText('Auto-Plan berechnen')

    await user().click(screen.getByRole('button', { name: 'Auto-Plan berechnen' }))
    await screen.findByText(/ScheduleProposal · Diff/)

    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    await waitFor(() =>
      expect(lastBody('POST', `/api/tournaments/${T}/schedule/confirm`)).toEqual({
        assignments: [
          {
            matchId: fx.IDS.match1,
            courtId: fx.IDS.court1,
            sequenceOnCourt: 1,
            plannedStart: '2026-05-16T08:00:00+00:00',
            estimatedDuration: '01:00:00',
          },
          {
            matchId: fx.IDS.match2,
            courtId: fx.IDS.court1,
            sequenceOnCourt: 2,
            plannedStart: '2026-05-16T09:30:00+00:00',
            estimatedDuration: '01:00:00',
          },
        ],
      }),
    )
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Vorschlag übernommen — 1 verschoben, 1 neu, 0 unberührt',
    )
    expect(screen.queryByText(/ScheduleProposal · Diff/)).not.toBeInTheDocument()
  })

  it('verwirft den Vorschlag auf Wunsch', async () => {
    aufbau()
    await screen.findByText('Auto-Plan berechnen')

    await user().click(screen.getByRole('button', { name: 'Auto-Plan berechnen' }))
    await screen.findByText(/ScheduleProposal · Diff/)

    await user().click(screen.getByRole('button', { name: 'Verwerfen' }))

    expect(screen.queryByText(/ScheduleProposal · Diff/)).not.toBeInTheDocument()
    expect(callsTo('POST', `/api/tournaments/${T}/schedule/confirm`)).toBe(0)
  })

  it('meldet einen gescheiterten Lauf', async () => {
    server.use(
      http.post(`/api/tournaments/${T}/schedule/proposal`, () =>
        HttpResponse.json(
          { detail: 'Keine Platzzeiten hinterlegt.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByText('Auto-Plan berechnen')

    await user().click(screen.getByRole('button', { name: 'Auto-Plan berechnen' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Auto-Plan: Keine Platzzeiten hinterlegt.',
    )
  })

  it('meldet eine gescheiterte Übernahme', async () => {
    server.use(
      http.post(`/api/tournaments/${T}/schedule/confirm`, () =>
        HttpResponse.json(
          { detail: 'Zwischenzeitlich geändert.', status: 409 },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    aufbau()
    await screen.findByText('Auto-Plan berechnen')

    await user().click(screen.getByRole('button', { name: 'Auto-Plan berechnen' }))
    await screen.findByText(/ScheduleProposal · Diff/)
    await user().click(screen.getByRole('button', { name: 'Übernehmen' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Übernahme: Zwischenzeitlich geändert')
  })

  it('rechnet am Turniertag nicht neu und sagt auch warum', async () => {
    aufbau({ schedulingMode: SchedulingMode.MatchDay })
    await screen.findByText(/Turniertag: kein Zeitraster/)

    const knopf = screen.getByRole('button', { name: 'Auto-Plan berechnen' })
    expect(knopf).toBeDisabled()
    expect(knopf).toHaveAttribute(
      'title',
      'Im Turniertagmodus wird nicht neu gerechnet — die Reihenfolge auf dem Platz ist die Aussage.',
    )
  })
})

describe('BoardScreen — Tageswahl', () => {
  it('bietet sie nur bei mehreren Turniertagen an', async () => {
    aufbau({ startsOn: '2026-05-16', endsOn: '2026-05-16' })
    await screen.findByText('Auto-Plan berechnen')

    expect(screen.queryByLabelText('Turniertag')).not.toBeInTheDocument()
  })

  it('wechselt den gezeigten Tag', async () => {
    db.phases = [fx.phase({ matches: [angesetzt()] })]
    aufbau()
    await screen.findByLabelText('Turniertag')

    expect(screen.getByLabelText('Turniertag')).toHaveValue('2026-05-16')

    await user().selectOptions(screen.getByLabelText('Turniertag'), '2026-05-17')
    expect(screen.getByLabelText('Turniertag')).toHaveValue('2026-05-17')
  })

  it('nimmt den heutigen Tag, wenn das Turnier gerade läuft', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.setSystemTime(new Date(2026, 4, 17, 9))

    db.phases = [fx.phase({ matches: [angesetzt()] })]
    aufbau()

    await waitFor(() => expect(screen.getByLabelText('Turniertag')).toHaveValue('2026-05-17'))
    vi.useRealTimers()
  })

  it('nimmt den Tag, an dem etwas angesetzt ist, vor dem ersten', async () => {
    db.phases = [
      fx.phase({ matches: [angesetzt({ plannedStart: '2026-05-17T08:00:00+00:00' })] }),
    ]
    aufbau()

    await waitFor(() => expect(screen.getByLabelText('Turniertag')).toHaveValue('2026-05-17'))
  })

  it('kommt ohne Termin ohne Tageswähler aus', async () => {
    aufbau({ startsOn: null, endsOn: null })
    await screen.findByText('Auto-Plan berechnen')

    expect(screen.queryByLabelText('Turniertag')).not.toBeInTheDocument()
  })

  it('lässt eine Karte ohne Uhrzeit stehen, statt sie verschwinden zu lassen', async () => {
    db.phases = [
      fx.phase({
        matches: [angesetzt({ plannedStart: null, earliestStart: null, actualStart: null })],
      }),
    ]
    aufbau()

    // Sie hat keine Zeit und damit keine Position — sichtbar ist sie trotzdem
    // nicht, weil das Raster ohne Uhrzeit nichts zeichnen kann. Geprüft wird,
    // dass der Screen daran nicht scheitert.
    await screen.findByText('Auto-Plan berechnen')
    expect(document.querySelector('.md-gantt__lane')).not.toBeNull()
  })
})

describe('BoardScreen — Umhängen', () => {
  it('setzt eine Zuweisung von Hand und pinnt sie', async () => {
    db.phases = [fx.phase({ matches: [angesetzt()] })]
    aufbau()

    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())

    const spalten = [...document.querySelectorAll('div[style*="position: relative"]')]
    fireEvent.dragStart(document.querySelector('.md-gantt__card')!)
    fireEvent.drop(spalten[1]!)

    await waitFor(() =>
      expect(lastBody('POST', `/api/matches/${fx.IDS.match1}/court`)).toEqual({
        courtId: fx.IDS.court2,
        sequenceOnCourt: 0,
        plannedStart: '2026-05-16T08:00:00+00:00',
        earliestStart: null,
        estimatedDuration: '01:00:00',
        pinned: true,
      }),
    )
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Zuweisung manuell gesetzt → als harter Constraint gepinnt',
    )
  })

  it('zählt die Zuweisungen, die auf dem Zielplatz schon liegen', async () => {
    db.phases = [
      fx.phase({
        matches: [
          angesetzt(),
          fx.match({
            id: fx.IDS.match2,
            assignment: fx.assignment({
              id: fx.IDS.assignment2,
              courtId: fx.IDS.court2,
              courtName: 'Platz 2',
            }),
          }),
        ],
      }),
    ]
    aufbau()
    await waitFor(() => expect(document.querySelectorAll('.md-gantt__card')).toHaveLength(2))

    const spalten = [...document.querySelectorAll('div[style*="position: relative"]')]
    fireEvent.dragStart(document.querySelectorAll('.md-gantt__card')[0]!)
    fireEvent.drop(spalten[1]!)

    await waitFor(() =>
      expect(lastBody('POST', `/api/matches/${fx.IDS.match1}/court`)).toMatchObject({
        sequenceOnCourt: 1,
      }),
    )
  })

  it('nennt die Verstöße, ohne den Zug zu verhindern', async () => {
    server.use(
      http.post(`/api/matches/:matchId/court`, () =>
        HttpResponse.json({
          assignmentId: fx.IDS.assignment1,
          violations: [
            {
              constraint: ScheduleConstraint.PlayerDoubleBooked,
              message: 'x',
              assignmentId: fx.IDS.assignment1,
            },
          ],
        }),
      ),
    )
    db.phases = [fx.phase({ matches: [angesetzt()] })]
    aufbau()
    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())

    const spalten = [...document.querySelectorAll('div[style*="position: relative"]')]
    fireEvent.dragStart(document.querySelector('.md-gantt__card')!)
    fireEvent.drop(spalten[1]!)

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Zuweisung gesetzt & gepinnt — mit 1 Verstoß: Spieler doppelt angesetzt',
    )
  })

  it('setzt den Plural, wo es mehrere sind', async () => {
    server.use(
      http.post(`/api/matches/:matchId/court`, () =>
        HttpResponse.json({
          assignmentId: fx.IDS.assignment1,
          violations: [
            { constraint: ScheduleConstraint.PlayerDoubleBooked, message: 'x', assignmentId: 'a' },
            { constraint: ScheduleConstraint.CourtUnavailable, message: 'y', assignmentId: 'b' },
          ],
        }),
      ),
    )
    db.phases = [fx.phase({ matches: [angesetzt()] })]
    aufbau()
    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())

    const spalten = [...document.querySelectorAll('div[style*="position: relative"]')]
    fireEvent.dragStart(document.querySelector('.md-gantt__card')!)
    fireEvent.drop(spalten[1]!)

    expect(await screen.findByRole('status')).toHaveTextContent(
      'mit 2 Verstößen: Spieler doppelt angesetzt, Platz nicht verfügbar',
    )
  })

  it('nimmt für ein Match ohne Schätzung eine Vorgabedauer', async () => {
    db.phases = [fx.phase({ matches: [fx.match({ assignment: null })] })]
    db.board = fx.matchDayBoard([
      fx.courtBoard({ queue: [fx.queuedMatch()] }),
      fx.courtBoard({ courtId: fx.IDS.court2, courtName: 'Platz 2', queue: [] }),
    ])
    aufbau({ schedulingMode: SchedulingMode.MatchDay })

    await waitFor(() => expect(document.querySelector('.md-queue__card')).not.toBeNull())

    const spalten = [...document.querySelectorAll('.md-queue__col')]
    fireEvent.dragStart(document.querySelector('.md-queue__card')!)
    fireEvent.drop(spalten[1]!)

    await waitFor(() =>
      expect(lastBody('POST', `/api/matches/${fx.IDS.match1}/court`)).toMatchObject({
        estimatedDuration: '01:15:00',
        plannedStart: null,
        earliestStart: null,
      }),
    )
  })

  it('lässt einen Wurf auf ein unbekanntes Match auf sich beruhen', async () => {
    db.phases = [fx.phase({ matches: [] })]
    db.board = fx.matchDayBoard([fx.courtBoard(), fx.courtBoard({ courtId: fx.IDS.court2, courtName: 'Platz 2', queue: [] })])
    aufbau({ schedulingMode: SchedulingMode.MatchDay })

    await waitFor(() => expect(document.querySelector('.md-queue__card')).not.toBeNull())

    const spalten = [...document.querySelectorAll('.md-queue__col')]
    fireEvent.dragStart(document.querySelector('.md-queue__card')!)
    fireEvent.drop(spalten[1]!)

    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(callsTo('POST', `/api/matches/${fx.IDS.match1}/court`)).toBe(0)
  })

  it('meldet eine abgewiesene Zuweisung', async () => {
    server.use(
      http.post(`/api/matches/:matchId/court`, () =>
        HttpResponse.json(
          { detail: 'Der Platz ist gesperrt.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    db.phases = [fx.phase({ matches: [angesetzt()] })]
    aufbau()
    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())

    const spalten = [...document.querySelectorAll('div[style*="position: relative"]')]
    fireEvent.dragStart(document.querySelector('.md-gantt__card')!)
    fireEvent.drop(spalten[1]!)

    expect(await screen.findByRole('status')).toHaveTextContent('Zuweisung: Der Platz ist gesperrt.')
  })
})

describe('BoardScreen — Turniertag', () => {
  function amPlatz() {
    return aufbau({ schedulingMode: SchedulingMode.MatchDay })
  }

  it('zeigt die Warteschlangen statt des Rasters', async () => {
    amPlatz()

    expect(await screen.findByText('Platz 1')).toBeInTheDocument()
    expect(document.querySelector('.md-queue')).not.toBeNull()
    expect(document.querySelector('.md-gantt__lane')).toBeNull()
  })

  it('ruft auf, startet, gibt frei, unterbricht und setzt fort', async () => {
    const faelle: [AssignmentStatus, string, string, string][] = [
      [AssignmentStatus.Planned, 'Aufrufen', 'call', 'Aufruf ausgehängt & gepusht'],
      [AssignmentStatus.Called, 'Start', 'start', 'Match gestartet — Schätzungen der Wartenden werden nachgezogen'],
      [AssignmentStatus.Running, 'Platz frei', 'finish', 'Platz frei — die Warteschlange rückt nach. Das Ergebnis wird getrennt eingetragen.'],
      [AssignmentStatus.Running, 'Pause', 'suspend', 'Unterbrochen — Wiederaufnahme auf beliebigem Platz möglich'],
      [AssignmentStatus.Suspended, 'Fortsetzen', 'resume', 'Fortgesetzt — die unterbrochene Zuweisung bleibt als Historie stehen'],
    ]

    for (const [status, knopf, segment, meldung] of faelle) {
      db.board = fx.matchDayBoard([fx.courtBoard({ queue: [fx.queuedMatch({ status })] })])
      const { unmount } = renderWithProviders(
        <>
          <BoardScreen />
          <Toast />
        </>,
        {
          workspace: workspace({
            tournament: fx.tournamentDetail({
              state: TournamentState.InProgress,
              schedulingMode: SchedulingMode.MatchDay,
            }),
          }),
        },
      )

      await user().click(await screen.findByRole('button', { name: knopf }))

      await waitFor(() =>
        expect(callsTo('POST', `/api/assignments/${fx.IDS.assignment1}/${segment}`)).toBe(1),
      )
      expect(await screen.findByRole('status')).toHaveTextContent(meldung)

      unmount()
      db.calls = []
    }
  })

  it('öffnet die Ergebniseingabe zum Match der Zuweisung', async () => {
    db.board = fx.matchDayBoard([
      fx.courtBoard({ current: fx.queuedMatch({ status: AssignmentStatus.Running }), queue: [] }),
    ])
    amPlatz()

    await user().click(await screen.findByRole('button', { name: 'Ergebnis' }))

    expect(await screen.findByRole('dialog', { name: 'Ergebnis erfassen' })).toBeInTheDocument()
  })

  it('sagt es, wenn das Match nicht im geladenen Bracket steht', async () => {
    db.phases = [fx.phase({ matches: [] })]
    db.board = fx.matchDayBoard([
      fx.courtBoard({ current: fx.queuedMatch({ status: AssignmentStatus.Running }), queue: [] }),
    ])
    amPlatz()

    await user().click(await screen.findByRole('button', { name: 'Ergebnis' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Ergebnis: Match nicht im geladenen Bracket gefunden.',
    )
  })

  it('meldet einen abgewiesenen Aufruf und lädt trotzdem nach', async () => {
    server.use(
      http.post(`/api/assignments/:assignmentId/call`, () =>
        HttpResponse.json(
          { detail: 'Der Platz ist belegt.', status: 422 },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    amPlatz()

    await user().click(await screen.findByRole('button', { name: 'Aufrufen' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Turniertag: Der Platz ist belegt.')
    await waitFor(() => expect(callsTo('GET', `/api/tournaments/${T}/courts`)).toBeGreaterThan(1))
  })

  it('meldet einen Fehler beim Laden der Plätze', async () => {
    server.use(http.get('/api/tournaments/:id/courts', () => new HttpResponse(null, { status: 503 })))
    amPlatz()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
  })

  it('zeigt die Ladeanzeige, solange die Plätze fehlen', async () => {
    let freigeben: () => void = () => {}
    server.use(
      http.get('/api/tournaments/:id/courts', async () => {
        await new Promise<void>((resolve) => {
          freigeben = resolve
        })
        return HttpResponse.json(db.board)
      }),
    )
    amPlatz()

    expect(await screen.findByText('Plätze werden geladen …')).toBeInTheDocument()
    freigeben()
    await screen.findByText('Platz 1')
  })
})

describe('BoardScreen — Ergebniseingabe', () => {
  it('nennt Platz und geschätzte Dauer', async () => {
    db.phases = [fx.phase({ matches: [angesetzt()] })]
    aufbau()

    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())
    await user().click(document.querySelector('.md-gantt__card') as HTMLElement)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Platz 1 · ≈ 60 min')).toBeInTheDocument()
  })

  it('nennt die Folgerunde, wo es eine gibt', async () => {
    db.phases = [
      fx.phase({
        matches: [angesetzt(), fx.match({ id: fx.IDS.match3, round: 2, label: 'F' })],
      }),
    ]
    aufbau()

    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())
    await user().click(document.querySelector('.md-gantt__card') as HTMLElement)

    expect(await screen.findByText(/Refs in Runde 3 auf/)).toBeInTheDocument()
  })

  it('sagt in der letzten Runde, dass das Turnier damit endet', async () => {
    db.phases = [fx.phase({ matches: [angesetzt()] })]
    aufbau()

    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())
    await user().click(document.querySelector('.md-gantt__card') as HTMLElement)

    expect(
      await screen.findByText('Letzte Runde — mit dem Ergebnis wechselt das Turnier nach Completed.'),
    ).toBeInTheDocument()
  })

  it('kennt die Folgerunde nicht, wo die Phase fehlt', async () => {
    db.board = fx.matchDayBoard([
      fx.courtBoard({ current: fx.queuedMatch({ status: AssignmentStatus.Running }), queue: [] }),
    ])
    db.phases = [fx.phase({ id: 'f0000000-0000-0000-0000-000000000099', matches: [fx.match()] })]
    aufbau({ schedulingMode: SchedulingMode.MatchDay })

    await user().click(await screen.findByRole('button', { name: 'Ergebnis' }))

    expect(
      await screen.findByText('Letzte Runde — mit dem Ergebnis wechselt das Turnier nach Completed.'),
    ).toBeInTheDocument()
  })

  it('lädt nach dem Speichern Spielplan und Turnier nach', async () => {
    db.phases = [fx.phase({ matches: [angesetzt()] })]
    const { reloadTournament } = aufbau()

    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())
    await user().click(document.querySelector('.md-gantt__card') as HTMLElement)

    const u = user()
    const erhoehen = (name: string) => screen.getByRole('button', { name: `${name} erhöhen` })
    for (let i = 0; i < 6; i++) await u.click(erhoehen('S. Moser, Satz 1'))
    for (let i = 0; i < 4; i++) await u.click(erhoehen('L. Berger, Satz 1'))
    for (let i = 0; i < 6; i++) await u.click(erhoehen('S. Moser, Satz 2'))
    for (let i = 0; i < 3; i++) await u.click(erhoehen('L. Berger, Satz 2'))

    await u.click(screen.getByRole('button', { name: 'Speichern & propagieren' }))

    await waitFor(() => expect(reloadTournament).toHaveBeenCalled())
  })

  it('nimmt das Satzformat aus dem eingefrorenen Snapshot der Phase', async () => {
    db.phases = [fx.phase({ ordinal: 1, matches: [angesetzt()] })]
    aufbau({
      format: {
        templateId: fx.IDS.template,
        templateVersion: 1,
        definition: fx.formatDefinition({
          phases: [
            {
              ordinal: 1,
              format: PhaseFormatKind.Knockout,
              matchFormat: { bestOf: 1, finalSetMode: FinalSetMode.Regular, tiebreakAt: 4 },
            },
          ],
        }),
      },
    })

    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())
    await user().click(document.querySelector('.md-gantt__card') as HTMLElement)

    expect(await screen.findByText('Satz 1')).toBeInTheDocument()
    expect(screen.queryByText('Satz 2')).not.toBeInTheDocument()
  })

  it('nimmt für ein Match ohne Etikett den Anfang seiner Kennung', async () => {
    db.phases = [fx.phase({ matches: [fx.match({ ...angesetzt(), label: null })] })]
    aufbau()

    await waitFor(() => expect(document.querySelector('.md-gantt__card')).not.toBeNull())
    await user().click(document.querySelector('.md-gantt__card') as HTMLElement)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(fx.IDS.match1.slice(0, 8))).toBeInTheDocument()
  })
})
