import { screen, waitFor, within } from '@testing-library/react'
import { HttpResponse, http } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import type { PublicMatchView, PublicTournamentView } from '../api/types'
import * as fx from '../test/fixtures'
import { renderWithProviders, user, workspace } from '../test/render'
import { db, server } from '../test/server'
import { useNarrowScreen } from '../test/setup'
import { PublicScreen } from './PublicScreen'

const T = fx.IDS.tournament

/**
 * Der Push-Kanal ist hier eine Attrappe. Er hat einen eigenen Test; was diese
 * Seite betrifft, ist allein, ob sie „live" oder „poll" anzeigt.
 */
let melde: (() => void) | null = null
let zustand: ((connected: boolean) => void) | null = null

vi.mock('../api/realtime', () => ({
  PROJECTION_CHANGED: 'projectionChanged',
  subscribeToTournament: (
    _id: string,
    onChanged: () => void,
    onConnectionState?: (connected: boolean) => void,
  ) => {
    melde = onChanged
    zustand = onConnectionState ?? null
    return () => {}
  },
}))

function imArbeitsbereich(over: Parameters<typeof workspace>[0] = {}) {
  return renderWithProviders(<PublicScreen />, { workspace: workspace(over) })
}

function alsZuschauer(props: Parameters<typeof PublicScreen>[0] = {}) {
  return renderWithProviders(<PublicScreen standalone {...props} />, { workspace: null })
}

/** Ein Zuschauerlink in der Adresszeile. */
function mitLink(zusatz = ''): void {
  window.history.replaceState({}, '', `/?t=${T}${zusatz}`)
}

function setzeAnsicht(over: Partial<PublicTournamentView>): void {
  db.publicView = fx.publicView(over)
}

/** Auf einen Reiter wechseln — er entsteht erst mit der geholten Ansicht. */
async function reiter(name: string): Promise<void> {
  await user().click(await screen.findByRole('button', { name }))
}

describe('PublicScreen — Zugang', () => {
  it('nimmt im Arbeitsbereich das gewählte Turnier', async () => {
    imArbeitsbereich()
    expect(await screen.findByText('Jetzt am Platz')).toBeInTheDocument()
  })

  it('nimmt ohne Anmeldung die Turnier-Id aus der Adresszeile', async () => {
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('Clubmeisterschaft 2026')).toBeInTheDocument()
  })

  it('nimmt eine mitgegebene Id vor allem anderen', async () => {
    alsZuschauer({ tournamentId: T })
    expect(await screen.findByText('Clubmeisterschaft 2026')).toBeInTheDocument()
  })

  it('sagt ohne Id, wie die Adresse aussehen muss', () => {
    alsZuschauer()

    expect(screen.getByText('Kein Turnier')).toBeInTheDocument()
    expect(screen.getByText(/Die Adresse braucht die Turnier-Id/)).toBeInTheDocument()
    expect(screen.getByText('Kein Turnier geladen')).toBeInTheDocument()
  })

  it('sagt im Arbeitsbereich schlicht, dass keines gewählt ist', () => {
    imArbeitsbereich({ tournament: null })
    expect(screen.getByText('Kein Turnier ausgewählt.')).toBeInTheDocument()
  })

  it('bietet dem Zuschauer den einen Weg hinaus', async () => {
    mitLink()
    const onClick = vi.fn()
    alsZuschauer({ action: { label: 'Anmelden', onClick } })

    await user().click(await screen.findByRole('button', { name: 'Anmelden' }))
    expect(onClick).toHaveBeenCalled()
  })

  it('zeigt die Ladeanzeige, solange nichts da ist', () => {
    mitLink()
    alsZuschauer()
    expect(screen.getByRole('status')).toHaveTextContent('Wird geholt …')
  })

  it('meldet einen Fehler und bietet einen zweiten Anlauf', async () => {
    server.use(http.get('/public/tournaments/:id', () => new HttpResponse(null, { status: 503 })))
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('Konnte nicht geladen werden')).toBeInTheDocument()
    await user().click(screen.getByRole('button', { name: 'Erneut versuchen' }))
  })

  it('sagt vor der Auslosung, dass es nichts zu zeigen gibt', async () => {
    server.use(http.get('/public/tournaments/:id', () => HttpResponse.json(null)))
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('Noch keine öffentliche Ansicht')).toBeInTheDocument()
  })

  it('zeigt im Arbeitsbereich Kanal und ETag-Ersparnis', async () => {
    imArbeitsbereich()
    await screen.findByText('Jetzt am Platz')

    expect(screen.getByText('poll')).toBeInTheDocument()
    expect(screen.getByText('304')).toBeInTheDocument()
  })

  it('meldet den stehenden Push-Kanal', async () => {
    mitLink()
    alsZuschauer()
    await waitFor(() => expect(zustand).not.toBeNull())

    const { container } = alsZuschauer()
    void container
    zustand?.(true)

    await waitFor(() => expect(document.querySelector('.md-live-dot')).not.toBeNull())
  })

  it('zeigt „live", sobald der Push-Kanal steht', async () => {
    imArbeitsbereich()
    await screen.findByText('Jetzt am Platz')

    zustand?.(true)

    expect(await screen.findByText('live')).toBeInTheDocument()
  })

  it('holt neu, wenn der Hub etwas meldet', async () => {
    mitLink()
    alsZuschauer()
    await screen.findByText('Clubmeisterschaft 2026')

    db.publicEtag = '"etag-2"'
    setzeAnsicht({ name: 'Umbenannt' })
    melde?.()

    expect(await screen.findByText('Umbenannt')).toBeInTheDocument()
  })

  it('nimmt die Zeitzone aus der Antwort und nicht die des Browsers', async () => {
    setzeAnsicht({
      timeZoneId: 'UTC',
      phases: [
        fx.publicPhase({
          matches: [fx.publicMatch({ plannedStart: '2026-05-16T08:00:00+00:00' })],
        }),
      ],
    })
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('~08:00')).toBeInTheDocument()
  })

  it('fällt auf die Zone der Anwendung zurück, wo die Antwort keine führt', async () => {
    setzeAnsicht({
      timeZoneId: null,
      phases: [
        fx.publicPhase({
          matches: [fx.publicMatch({ plannedStart: '2026-05-16T08:00:00+00:00' })],
        }),
      ],
    })
    imArbeitsbereich()

    expect(await screen.findByText('~10:00')).toBeInTheDocument()
  })

  it('nimmt ohne Arbeitsbereich die Zone dieser Anwendung', async () => {
    setzeAnsicht({
      timeZoneId: null,
      phases: [
        fx.publicPhase({
          matches: [fx.publicMatch({ plannedStart: '2026-05-16T08:00:00+00:00' })],
        }),
      ],
    })
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('~10:00')).toBeInTheDocument()
  })
})

describe('PublicScreen — Reiter', () => {
  it('lässt Tabellen weg, wo es keine gibt', async () => {
    mitLink()
    alsZuschauer()
    await screen.findByText('Clubmeisterschaft 2026')

    expect(screen.queryByRole('button', { name: 'Tabellen' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Plätze' })).toBeInTheDocument()
  })

  it('zeigt Tabellen, sobald eine Phase welche führt', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          standings: [
            {
              rank: 1,
              name: 'S. Moser',
              group: 'A',
              played: 2,
              won: 2,
              lost: 0,
              points: 4,
              setsWon: 4,
              setsLost: 1,
              gamesWon: 26,
              gamesLost: 14,
            },
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()

    expect(await screen.findByRole('button', { name: 'Tabellen' })).toBeInTheDocument()
  })

  it('lässt Plätze weg, wo keiner hinterlegt ist', async () => {
    setzeAnsicht({ courts: [] })
    mitLink()
    alsZuschauer()
    await screen.findByText('Clubmeisterschaft 2026')

    expect(screen.queryByRole('button', { name: 'Plätze' })).not.toBeInTheDocument()
  })

  it('fällt auf „Live" zurück, wo der offene Reiter verschwindet', async () => {
    mitLink()
    alsZuschauer()
    await reiter('Plätze')
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Plätze' })).toHaveAttribute('aria-current', 'page'),
    )

    db.publicEtag = '"etag-2"'
    setzeAnsicht({ courts: [] })
    melde?.()

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Live' })).toHaveAttribute('aria-current', 'page'),
    )
  })
})

describe('PublicScreen — Live', () => {
  function laufend(over: Partial<PublicMatchView> = {}) {
    return fx.publicMatch({ assignmentStatus: 'Running', score: '6:4, 3:2', ...over })
  }

  it('sagt, wenn gerade nichts läuft', async () => {
    setzeAnsicht({ phases: [fx.publicPhase({ matches: [fx.publicMatch()] })] })
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('Gerade läuft nichts')).toBeInTheDocument()
  })

  it('zeigt das laufende Match mit Platz und Spielstand je Seite', async () => {
    setzeAnsicht({ phases: [fx.publicPhase({ matches: [laufend()] })] })
    mitLink()
    alsZuschauer()

    const karte = (await screen.findByText('Platz 1')).closest('.md-live-card') as HTMLElement
    expect(within(karte).getByText('läuft')).toBeInTheDocument()
    expect(within(karte).getByText('6 3')).toBeInTheDocument()
    expect(within(karte).getByText('4 2')).toBeInTheDocument()
  })

  it('nennt eine Zusage beim Aufruf', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            laufend({ assignmentStatus: 'Called', earliestStart: '2026-05-16T12:00:00+00:00' }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('Aufgerufen · nicht vor 14:00')).toBeInTheDocument()
  })

  it('sagt beim Aufruf ohne Zusage nur „Aufgerufen"', async () => {
    setzeAnsicht({
      phases: [fx.publicPhase({ matches: [laufend({ assignmentStatus: 'Called' })] })],
    })
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('Aufgerufen')).toBeInTheDocument()
  })

  it('erklärt eine unterbrochene Partie', async () => {
    setzeAnsicht({
      phases: [fx.publicPhase({ matches: [laufend({ assignmentStatus: 'Suspended' })] })],
    })
    mitLink()
    alsZuschauer()

    expect(
      await screen.findByText(/Unterbrochen — die Partie wird fortgesetzt/),
    ).toBeInTheDocument()
  })

  it('zeigt ohne Platz einen Gedankenstrich und ohne Etikett die Gruppe', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [laufend({ courtName: null, label: null, group: 'Gruppe A', score: null })],
        }),
      ],
    })
    mitLink()
    alsZuschauer()

    const karte = (await screen.findByText('Gruppe A')).closest('.md-live-card') as HTMLElement
    expect(within(karte).getByText('—')).toBeInTheDocument()
  })

  it('lässt Etikett und Gruppe leer, wo beides fehlt', async () => {
    setzeAnsicht({
      phases: [fx.publicPhase({ matches: [laufend({ label: null, group: null })] })],
    })
    mitLink()
    alsZuschauer()

    await screen.findByText('Platz 1')
    expect(document.querySelector('.md-live-card__label')).toBeEmptyDOMElement()
  })

  it('warnt im Planungsmodus, dass die Zeiten ein Plan sind', async () => {
    setzeAnsicht({ schedulingMode: 'Planning' })
    mitLink()
    alsZuschauer()

    expect(
      await screen.findByText('Das Turnier läuft im Planungsmodus — die Zeiten sind ein Plan, kein Aufruf.'),
    ).toBeInTheDocument()
  })

  it('schweigt darüber am Turniertag', async () => {
    mitLink()
    alsZuschauer()
    await screen.findByText('Jetzt am Platz')

    expect(screen.queryByText(/die Zeiten sind ein Plan/)).not.toBeInTheDocument()
  })

  it('sortiert die nächsten Ansetzungen nach Zusage vor Schätzung', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            fx.publicMatch({
              id: 'd0000000-0000-0000-0000-00000000000a',
              plannedStart: '2026-05-16T10:00:00+00:00',
              side1: { name: 'Später', seed: null, origin: 'x' },
            }),
            fx.publicMatch({
              id: 'd0000000-0000-0000-0000-00000000000b',
              earliestStart: '2026-05-16T08:00:00+00:00',
              plannedStart: null,
              side1: { name: 'Früher', seed: null, origin: 'x' },
            }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()

    await screen.findByText('Als nächstes')
    const zeilen = [...document.querySelectorAll('.md-rows__pair')].map((el) => el.textContent)
    expect(zeilen[0]).toContain('Früher')
    expect(zeilen[1]).toContain('Später')

    expect(screen.getByText('Zusage')).toBeInTheDocument()
    expect(screen.getByText('Schätzung')).toBeInTheDocument()
  })

  it('reiht ein Match ohne jede Zeitangabe ein, statt es zu verlieren', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            fx.publicMatch({
              id: 'd0000000-0000-0000-0000-00000000000c',
              earliestStart: null,
              plannedStart: null,
              side1: { name: 'Ohne Zeit', seed: null, origin: 'x' },
            }),
            fx.publicMatch({
              id: 'd0000000-0000-0000-0000-00000000000d',
              plannedStart: '2026-05-16T10:00:00+00:00',
              side1: { name: 'Mit Zeit', seed: null, origin: 'x' },
            }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()

    await screen.findByText('Als nächstes')
    const zeilen = [...document.querySelectorAll('.md-rows__pair')].map((el) => el.textContent)
    expect(zeilen[0]).toContain('Ohne Zeit')
  })

  it('erklärt den Unterschied zwischen Zusage und Schätzung', async () => {
    mitLink()
    alsZuschauer()

    expect(await screen.findByText(/Fette Zeiten sind Zusagen/)).toBeInTheDocument()
  })

  it('sagt es, wenn nichts mehr angesetzt ist', async () => {
    setzeAnsicht({ phases: [fx.publicPhase({ matches: [] })] })
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('Keine weiteren Ansetzungen.')).toBeInTheDocument()
    expect(screen.getByText('Noch kein Ergebnis.')).toBeInTheDocument()
  })

  it('zeigt „ohne Platz", wo einer fehlt', async () => {
    setzeAnsicht({
      phases: [fx.publicPhase({ matches: [fx.publicMatch({ courtName: null })] })],
    })
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('ohne Platz')).toBeInTheDocument()
  })

  it('nennt die zuletzt entschiedenen Partien mit dem Sieger zuerst', async () => {
    mitLink()
    alsZuschauer()

    expect(await screen.findByText('A. Huber d. T. Wagner')).toBeInTheDocument()
  })
})

describe('PublicScreen — Draw', () => {
  it('zeigt die Herkunft, wo noch niemand feststeht', async () => {
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    expect(screen.getAllByText('Sieger M1').length).toBeGreaterThan(0)
  })

  it('zeigt den Spielstand je Seite', async () => {
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    const karte = screen.getByTitle('A. Huber vs T. Wagner — 6:4, 6:3')
    const staende = [...karte.querySelectorAll('.md-bracket__score')].map((el) => el.textContent)
    expect(staende).toEqual(['6 6', '4 3'])
  })

  it('zeigt einen unzerlegbaren Stand als Ganzes beim Sieger', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            fx.publicMatch({
              status: 'Finished',
              outcome: 'Walkover',
              winnerSide: 2,
              score: 'kampflos',
            }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    const staende = [...document.querySelectorAll('.md-bracket__score')].map((el) => el.textContent)
    expect(staende).toEqual(['', 'kampflos'])
  })

  it('zeigt ihn beim ersten Sieger genauso', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            fx.publicMatch({
              status: 'Finished',
              outcome: 'Retirement',
              winnerSide: 1,
              score: 'Aufgabe',
            }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    const staende = [...document.querySelectorAll('.md-bracket__score')].map((el) => el.textContent)
    expect(staende).toEqual(['Aufgabe', ''])
  })

  it('lässt den Stand auch beim zweiten Sieger leer, wo keiner dasteht', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            fx.publicMatch({ status: 'Finished', outcome: 'Bye', winnerSide: 2, score: null }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    const staende = [...document.querySelectorAll('.md-bracket__score')].map((el) => el.textContent)
    expect(staende).toEqual(['', ''])
  })

  it('lässt den Stand leer, wo keiner dasteht', async () => {
    setzeAnsicht({ phases: [fx.publicPhase({ matches: [fx.publicMatch({ score: null })] })] })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    const staende = [...document.querySelectorAll('.md-bracket__score')].map((el) => el.textContent)
    expect(staende).toEqual(['', ''])
  })

  it('markiert ein laufendes Match und dämpft ein Freilos', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            fx.publicMatch({ assignmentStatus: 'Running' }),
            fx.publicMatch({
              id: fx.IDS.match2,
              position: 2,
              status: 'Finished',
              outcome: 'Bye',
              winnerSide: 1,
              score: null,
            }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    expect(document.querySelector('.md-bracket__match--running')).not.toBeNull()
    expect(document.querySelector('.md-bracket__match--bye')).not.toBeNull()
  })

  it('zeigt den Setzplatz, wo einer vergeben ist', async () => {
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    const seeds = [...document.querySelectorAll('.md-bracket__seed')].map((el) => el.textContent)
    expect(seeds).toContain('1')
    expect(seeds).toContain('')
  })

  it('nimmt die Nummer, wo es keine Etiketten gibt', async () => {
    setzeAnsicht({
      phases: [fx.publicPhase({ matches: [fx.publicMatch({ label: null })] })],
    })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    expect(screen.getByText('Runde 1')).toBeInTheDocument()
  })

  it('stellt am Handy die Runden untereinander', async () => {
    useNarrowScreen()
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    expect(document.querySelector('.md-draw__list')).not.toBeNull()
    const zaehler = [...document.querySelectorAll('.md-draw__round-count')].map((el) => el.textContent)
    expect(zaehler).toEqual(['1 von 2', '0 von 1'])
  })

  it('lässt zwischen Spalten und Liste umschalten', async () => {
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    await user().click(screen.getByRole('button', { name: 'Liste' }))
    expect(document.querySelector('.md-draw__list')).not.toBeNull()

    await user().click(screen.getByRole('button', { name: 'Rundenspalten' }))
    expect(document.querySelector('.md-draw__columns')).not.toBeNull()
  })

  it('zeigt den Phasenwähler nur bei mehreren Phasen', async () => {
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    expect(document.querySelector('.md-public__phases')).toBeNull()
  })

  it('wechselt zwischen den Phasen', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({ name: 'Gruppen', status: 'Completed' }),
        fx.publicPhase({
          id: 'f0000000-0000-0000-0000-000000000002',
          ordinal: 2,
          name: 'Endrunde',
          status: 'Running',
          matches: [fx.publicMatch({ id: fx.IDS.match3, label: 'F' })],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    // Die laufende Phase steht zuerst.
    expect(screen.getByRole('button', { name: 'Endrunde' })).toHaveAttribute('aria-pressed', 'true')

    await user().click(screen.getByRole('button', { name: 'Gruppen' }))
    expect(screen.getByRole('button', { name: 'Gruppen' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('nimmt ohne laufende Phase die letzte mit Matches', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({ name: 'Gruppen', status: 'Completed' }),
        fx.publicPhase({
          id: 'f0000000-0000-0000-0000-000000000002',
          ordinal: 2,
          name: 'Endrunde',
          status: 'Pending',
          matches: [],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    expect(screen.getByRole('button', { name: 'Gruppen' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('sagt es, wo eine Phase keine Matches hat', async () => {
    setzeAnsicht({ phases: [fx.publicPhase({ matches: [] })] })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    expect(screen.getByText('Kein Draw')).toBeInTheDocument()
  })

  it('sagt es auch, wo es gar keine Phase gibt', async () => {
    setzeAnsicht({ phases: [] })
    mitLink()
    alsZuschauer()
    await reiter('Draw')

    expect(screen.getByText('Kein Draw')).toBeInTheDocument()
  })
})

describe('PublicScreen — Tabellen', () => {
  function mitTabelle(group: string | null) {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          standings: [
            {
              rank: 1,
              name: 'S. Moser',
              group,
              played: 2,
              won: 2,
              lost: 0,
              points: 4,
              setsWon: 4,
              setsLost: 1,
              gamesWon: 26,
              gamesLost: 14,
            },
          ],
        }),
      ],
    })
  }

  it('zeigt Rang, Punkte, Sätze und Spiele', async () => {
    mitTabelle('A')
    mitLink()
    alsZuschauer()
    await reiter('Tabellen')

    expect(screen.getByText('A')).toBeInTheDocument()
    expect(screen.getByText('S. Moser')).toBeInTheDocument()
    expect(screen.getByText('2–0')).toBeInTheDocument()
    expect(screen.getByText('4:1')).toBeInTheDocument()
    expect(screen.getByText('26:14')).toBeInTheDocument()
  })

  it('kommt ohne Gruppennamen aus', async () => {
    mitTabelle(null)
    mitLink()
    alsZuschauer()
    await reiter('Tabellen')

    expect(document.querySelector('.md-table-wrap__title')).toBeNull()
    expect(screen.getByText('S. Moser')).toBeInTheDocument()
  })
})

describe('PublicScreen — Ergebnisse', () => {
  it('stellt die jüngste Runde nach oben', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            fx.publicMatch({
              label: 'M1',
              status: 'Finished',
              winnerSide: 1,
              score: '6:1 6:1',
            }),
            fx.publicMatch({
              id: fx.IDS.match3,
              round: 2,
              label: 'F',
              status: 'Finished',
              winnerSide: 2,
              score: '7:5 6:4',
              side1: { name: 'A', seed: null, origin: 'x' },
              side2: { name: 'B', seed: null, origin: 'y' },
            }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Ergebnisse')

    const runden = [...document.querySelectorAll('.md-eyebrow')].map((el) => el.textContent)
    expect(runden[0]).toBe('F')
    expect(screen.getByText('B d. A')).toBeInTheDocument()
  })

  it('nennt, was am Ergebnis mehr sagt als der Spielstand', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [
            fx.publicMatch({
              status: 'Finished',
              outcome: 'Retirement',
              winnerSide: 1,
              score: '6:1 2:0',
            }),
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Ergebnisse')

    expect(screen.getByText('Retirement')).toBeInTheDocument()
  })

  it('schweigt über einen normalen Ausgang', async () => {
    mitLink()
    alsZuschauer()
    await reiter('Ergebnisse')

    expect(screen.queryByText('Normal')).not.toBeInTheDocument()
  })

  it('nennt beide Seiten, wo kein Sieger feststeht', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [fx.publicMatch({ status: 'Finished', winnerSide: null, score: null })],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Ergebnisse')

    expect(screen.getByText('S. Moser vs L. Berger')).toBeInTheDocument()
  })

  it('sagt es, solange nichts entschieden ist', async () => {
    setzeAnsicht({ phases: [fx.publicPhase({ matches: [fx.publicMatch()] })] })
    mitLink()
    alsZuschauer()
    await reiter('Ergebnisse')

    expect(screen.getByText('Noch kein Ergebnis')).toBeInTheDocument()
  })


})

describe('PublicScreen — Plätze', () => {
  it('zeigt, was auf dem Platz läuft, und wer wartet', async () => {
    setzeAnsicht({
      courts: [
        fx.publicCourt({
          queue: [
            { matchId: fx.IDS.match1, sequenceOnCourt: 1, status: 'Running', earliestStart: null, plannedStart: null },
            {
              matchId: fx.IDS.match3,
              sequenceOnCourt: 2,
              status: 'Planned',
              earliestStart: null,
              plannedStart: '2026-05-16T10:00:00+00:00',
            },
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Plätze')

    const karte = screen.getByText('Platz 1').closest('.md-live-card') as HTMLElement
    expect(within(karte).getByText('läuft')).toBeInTheDocument()
    expect(within(karte).getByText('S. Moser')).toBeInTheDocument()
    expect(within(karte).getByText('1.')).toBeInTheDocument()
    expect(within(karte).getByText('~12:00')).toBeInTheDocument()
  })

  it('zeigt eine Zusage in der Warteschlange aufrecht', async () => {
    setzeAnsicht({
      courts: [
        fx.publicCourt({
          queue: [
            { matchId: fx.IDS.match1, sequenceOnCourt: 1, status: 'Running', earliestStart: null, plannedStart: null },
            {
              matchId: fx.IDS.match3,
              sequenceOnCourt: 2,
              status: 'Planned',
              earliestStart: '2026-05-16T12:00:00+00:00',
              plannedStart: null,
            },
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Plätze')

    expect(screen.getByText('14:00')).toBeInTheDocument()
  })

  it('zeigt am Platz den Spielstand je Seite', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [fx.publicMatch({ assignmentStatus: 'Running', score: '6:4, 3:2' })],
        }),
      ],
      courts: [
        fx.publicCourt({
          queue: [
            { matchId: fx.IDS.match1, sequenceOnCourt: 1, status: 'Running', earliestStart: null, plannedStart: null },
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Plätze')

    const karte = screen.getByText('Platz 1').closest('.md-live-card') as HTMLElement
    expect(within(karte).getByText('6 3')).toBeInTheDocument()
    expect(within(karte).getByText('4 2')).toBeInTheDocument()
  })

  it('zählt Aufruf und Unterbrechung als „am Platz"', async () => {
    for (const status of ['Called', 'Suspended'] as const) {
      setzeAnsicht({
        courts: [
          fx.publicCourt({
            queue: [
              { matchId: fx.IDS.match1, sequenceOnCourt: 1, status, earliestStart: null, plannedStart: null },
            ],
          }),
        ],
      })
      mitLink()
      const { unmount } = alsZuschauer()
      await reiter('Plätze')

      const karte = screen.getByText('Platz 1').closest('.md-live-card') as HTMLElement
      expect(within(karte).getByText('S. Moser')).toBeInTheDocument()
      expect(within(karte).queryByText('frei')).not.toBeInTheDocument()

      unmount()
    }
  })

  it('sagt „frei", wo nichts läuft', async () => {
    setzeAnsicht({ courts: [fx.publicCourt({ queue: [] })] })
    mitLink()
    alsZuschauer()
    await reiter('Plätze')

    expect(screen.getByText('frei')).toBeInTheDocument()
    expect(screen.getByText('Kein Match am Platz')).toBeInTheDocument()
  })

  it('nennt ein unbekanntes Match in der Schlange schlicht „Match"', async () => {
    setzeAnsicht({
      courts: [
        fx.publicCourt({
          queue: [
            {
              matchId: 'd0000000-0000-0000-0000-0000000000ff',
              sequenceOnCourt: 1,
              status: 'Planned',
              earliestStart: null,
              plannedStart: null,
            },
            {
              matchId: 'd0000000-0000-0000-0000-0000000000fe',
              sequenceOnCourt: 2,
              status: 'Planned',
              earliestStart: null,
              plannedStart: null,
            },
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Plätze')

    expect(screen.getAllByText('Match').length).toBeGreaterThan(0)
  })

  it('führt ein fertiges Match nicht mehr als Wartenden', async () => {
    setzeAnsicht({
      courts: [
        fx.publicCourt({
          queue: [
            { matchId: fx.IDS.match1, sequenceOnCourt: 1, status: 'Finished', earliestStart: null, plannedStart: null },
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Plätze')

    expect(document.querySelector('.md-queue-list')).toBeNull()
  })
})

describe('PublicScreen — Tabellen ohne Inhalt', () => {
  it('sagt es, wenn die Tabellen unter der offenen Ansicht verschwinden', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          standings: [
            {
              rank: 1,
              name: 'S. Moser',
              group: 'A',
              played: 2,
              won: 2,
              lost: 0,
              points: 4,
              setsWon: 4,
              setsLost: 1,
              gamesWon: 26,
              gamesLost: 14,
            },
          ],
        }),
      ],
    })
    mitLink()
    alsZuschauer()
    await reiter('Tabellen')

    db.publicEtag = '"etag-2"'
    setzeAnsicht({ phases: [fx.publicPhase({ standings: [] })] })
    melde?.()

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Live' })).toHaveAttribute('aria-current', 'page'),
    )
  })
})

describe('PublicScreen — Clubhaus-Monitor', () => {
  it('startet auf Wunsch der Adresszeile im Monitorbetrieb', async () => {
    mitLink('&kiosk=1')
    alsZuschauer()

    await waitFor(() => expect(document.querySelector('.md-kiosk')).not.toBeNull())
    expect(screen.queryByRole('navigation', { name: 'Bereiche' })).not.toBeInTheDocument()
  })

  it('lässt sich vom Zuschauer umschalten', async () => {
    mitLink()
    alsZuschauer()
    await screen.findByText('Jetzt am Platz')

    await user().click(screen.getByRole('button', { name: 'Monitor' }))
    expect(document.querySelector('.md-kiosk')).not.toBeNull()

    await user().click(screen.getByRole('button', { name: 'Zuschauer' }))
    expect(document.querySelector('.md-kiosk')).toBeNull()
  })

  it('lässt sich auch im Arbeitsbereich umschalten', async () => {
    imArbeitsbereich()
    await screen.findByText('Jetzt am Platz')

    await user().click(screen.getByRole('button', { name: 'Clubhaus-Monitor' }))
    expect(document.querySelector('.md-kiosk')).not.toBeNull()

    await user().click(screen.getByRole('button', { name: 'Zuschauer-Ansicht' }))
    expect(document.querySelector('.md-kiosk')).toBeNull()
  })

  it('zeigt je Platz, was läuft und was danach kommt', async () => {
    setzeAnsicht({
      courts: [
        fx.publicCourt({
          queue: [
            { matchId: fx.IDS.match1, sequenceOnCourt: 1, status: 'Running', earliestStart: null, plannedStart: null },
            {
              matchId: fx.IDS.match3,
              sequenceOnCourt: 2,
              status: 'Planned',
              earliestStart: null,
              plannedStart: '2026-05-16T10:00:00+00:00',
            },
          ],
        }),
      ],
    })
    mitLink('&kiosk=1')
    alsZuschauer()

    await waitFor(() => expect(screen.getByText('Platz 1')).toBeInTheDocument())
    expect(screen.getByText('läuft')).toBeInTheDocument()
    expect(screen.getByText('S. Moser')).toBeInTheDocument()
    expect(screen.getByText(/Danach ~12:00 · Sieger M1 vs Sieger M2/)).toBeInTheDocument()
  })

  it('nennt eine Zusage als „ab"', async () => {
    setzeAnsicht({
      courts: [
        fx.publicCourt({
          queue: [
            { matchId: fx.IDS.match1, sequenceOnCourt: 1, status: 'Running', earliestStart: null, plannedStart: null },
            {
              matchId: fx.IDS.match3,
              sequenceOnCourt: 2,
              status: 'Planned',
              earliestStart: '2026-05-16T12:00:00+00:00',
              plannedStart: null,
            },
          ],
        }),
      ],
    })
    mitLink('&kiosk=1')
    alsZuschauer()

    expect(await screen.findByText(/Danach ab 14:00 ·/)).toBeInTheDocument()
  })

  it('lässt die Zeit weg, wo weder Zusage noch Schätzung steht', async () => {
    setzeAnsicht({
      courts: [
        fx.publicCourt({
          queue: [
            { matchId: fx.IDS.match1, sequenceOnCourt: 1, status: 'Running', earliestStart: null, plannedStart: null },
            {
              matchId: fx.IDS.match3,
              sequenceOnCourt: 2,
              status: 'Planned',
              earliestStart: null,
              plannedStart: null,
            },
          ],
        }),
      ],
    })
    mitLink('&kiosk=1')
    alsZuschauer()

    expect(await screen.findByText('Danach · Sieger M1 vs Sieger M2')).toBeInTheDocument()
  })

  it('sagt „frei" und „keine weiteren Matches", wo nichts ansteht', async () => {
    setzeAnsicht({ courts: [fx.publicCourt({ queue: [] })] })
    mitLink('&kiosk=1')
    alsZuschauer()

    expect(await screen.findByText('frei')).toBeInTheDocument()
    expect(screen.getByText('Keine weiteren Matches')).toBeInTheDocument()
    expect(screen.getAllByText('—')).toHaveLength(2)
  })

  it('kommt mit einem Platzhalter in der Schlange zurecht', async () => {
    setzeAnsicht({
      courts: [
        fx.publicCourt({
          queue: [
            {
              matchId: 'd0000000-0000-0000-0000-0000000000ff',
              sequenceOnCourt: 1,
              status: 'Running',
              earliestStart: null,
              plannedStart: null,
            },
            {
              matchId: 'd0000000-0000-0000-0000-0000000000fe',
              sequenceOnCourt: 2,
              status: 'Planned',
              earliestStart: null,
              plannedStart: null,
            },
          ],
        }),
      ],
    })
    mitLink('&kiosk=1')
    alsZuschauer()

    expect(await screen.findByText('Keine weiteren Matches')).toBeInTheDocument()
  })

  it('zeigt den Spielstand des laufenden Matches groß', async () => {
    setzeAnsicht({
      phases: [
        fx.publicPhase({
          matches: [fx.publicMatch({ assignmentStatus: 'Running', score: '6:4, 3:2' })],
        }),
      ],
    })
    mitLink('&kiosk=1')
    alsZuschauer()

    expect(await screen.findByText('6:4, 3:2')).toBeInTheDocument()
  })

  it('lässt die letzten Ergebnisse durchlaufen', async () => {
    mitLink('&kiosk=1')
    alsZuschauer()

    expect(await screen.findByText(/A\. Huber d\. T\. Wagner/)).toBeInTheDocument()
    expect(screen.getByText('6:4, 6:3')).toBeInTheDocument()
  })
})
